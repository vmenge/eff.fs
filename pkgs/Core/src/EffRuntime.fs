namespace EffSharp.Core

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

module internal EffRuntime =
  let private boxOption value = box value

  let mutable private nextFiberId = 0L

  type FiberLifecycle =
    | Running
    | Closing
    | Completed

  type CompletionClassification =
    | Normal
    | AbortCleanupFailure

  [<AllowNullLiteral>]
  type CompletionInbox<'t>() =
    let queue = ConcurrentQueue<'t>()
    let signal = new SemaphoreSlim(0)

    member _.Publish(value: 't) =
      queue.Enqueue(value)
      signal.Release() |> ignore

    member _.Receive() : Task<'t> =
      task {
        let mutable value = Unchecked.defaultof<'t>
        let mutable found = false

        while not found do
          if queue.TryDequeue(&value) then
            found <- true
          else
            do! signal.WaitAsync()

        return value
      }

    interface IDisposable with
      member _.Dispose() = signal.Dispose()

  [<Struct>]
  type BoxedExit =
    | BoxedOk of ok: obj
    | BoxedErr of err: obj
    | BoxedExn of exn: exn
    | BoxedAborted

  let private isFailure exit =
    match exit with
    | BoxedErr _
    | BoxedExn _ -> true
    | BoxedOk _
    | BoxedAborted -> false

  [<Struct>]
  type StepResult<'env> =
    | Continue of beff: BoxedEff<'env> * fr: Frame<'env> list
    | Done of bexit: BoxedExit
    | Await of
      tsk: Task<obj> * token: CancellationToken * awfn: (Result<obj, exn> -> StepResult<'env>)
    | Switch of machine: Machine * swfn: (BoxedExit -> StepResult<'env>)

  and [<AbstractClass>] Machine() =
    abstract Poll: unit -> MachinePoll

  and [<Struct>] MachinePoll =
    | MachineDone of bexit: BoxedExit
    | MachineAwait of
      tsk: Task<obj> * token: CancellationToken * resfn: (Result<obj, exn> -> Machine)
    | MachineSwitch of machine: Machine * resume: MachineResume

  and [<AbstractClass>] MachineResume() =
    abstract Resume: BoxedExit -> Machine

  and FiberHandle(parent: FiberHandle option, cts: CancellationTokenSource) =
    let id = Interlocked.Increment(&nextFiberId)
    let liveChildren = ConcurrentDictionary<int64, FiberHandle>()
    let stateGate = obj ()
    let mutable state = FiberLifecycle.Running
    let mutable completionTask = Unchecked.defaultof<Task<BoxedExit>>
    let mutable abortObserved = false
    let mutable completionClassification = CompletionClassification.Normal
    let mutable completionInbox =
      Unchecked.defaultof<CompletionInbox<struct (FiberHandle * BoxedExit)>>

    member _.Id = id
    member _.Parent = parent
    member _.CTS = cts
    member _.Token = cts.Token

    member _.CompletionTask =
      match completionTask with
      | null -> failwith "fiber completion task has not been attached"
      | task -> task

    member _.AttachTask(task: Task<BoxedExit>) =
      completionTask <- task

    member _.MarkAbortObserved() =
      abortObserved <- true

    member _.CompletionClassification = completionClassification

    member _.HasLiveChildren = not liveChildren.IsEmpty

    member _.TryRegisterChild(child: FiberHandle) =
      lock stateGate (fun () ->
        match state with
        | FiberLifecycle.Running -> liveChildren.TryAdd(child.Id, child)
        | FiberLifecycle.Closing
        | FiberLifecycle.Completed -> false)

    member _.DeregisterChild(child: FiberHandle) =
      liveChildren.TryRemove(child.Id) |> ignore

    member _.TryBeginClosing() =
      lock stateGate (fun () ->
        match state with
        | FiberLifecycle.Running ->
          state <- FiberLifecycle.Closing
          completionInbox <- new CompletionInbox<struct (FiberHandle * BoxedExit)>()
          true
        | FiberLifecycle.Closing
        | FiberLifecycle.Completed -> false)

    member _.TryGetCompletionInbox() =
      lock stateGate (fun () ->
        match completionInbox with
        | null -> None
        | inbox -> Some inbox)

    member _.PublishChildCompletion(child: FiberHandle, exit: BoxedExit) =
      lock stateGate (fun () ->
        match completionInbox with
        | null -> ()
        | inbox -> inbox.Publish(struct (child, exit)))

    member _.MarkCompleted() =
      lock stateGate (fun () -> state <- FiberLifecycle.Completed)

    member _.RequestAbort() =
      try
        cts.Cancel()
      with :? ObjectDisposedException ->
        ()

    member this.DrainChildren() : Task<BoxedExit option> =
      task {
        let mutable firstFailure = None
        let inbox =
          match this.TryGetCompletionInbox() with
          | Some inbox -> inbox
          | None -> failwith "fiber completion inbox is not available before draining children"
        let snapshot = liveChildren.Values |> Seq.toArray

        for child in snapshot do
          child.RequestAbort()

        while not liveChildren.IsEmpty do
          let! struct (child, childExit) = inbox.Receive()

          if
            Option.isNone firstFailure
            && child.CompletionClassification = CompletionClassification.AbortCleanupFailure
            && isFailure childExit
          then
            firstFailure <- Some childExit

        return firstFailure
      }

    member this.Complete(exit: BoxedExit) : Task<BoxedExit> =
      task {
        if this.HasLiveChildren then
          this.TryBeginClosing() |> ignore

        let! drainFailure =
          if this.HasLiveChildren then
            this.DrainChildren()
          else
            Task.FromResult None

        let finalExit =
          match drainFailure with
          | Some failure -> failure
          | None -> exit

        completionClassification <-
          if abortObserved && isFailure finalExit then
            CompletionClassification.AbortCleanupFailure
          else
            CompletionClassification.Normal

        match parent with
        | Some parentHandle ->
          parentHandle.DeregisterChild(this)
          parentHandle.PublishChildCompletion(this, finalExit)
        | None -> ()

        this.MarkCompleted()

        match completionInbox with
        | null -> ()
        | inbox ->
          (inbox :> IDisposable).Dispose()
          completionInbox <- null

        try
          cts.Dispose()
        with :? ObjectDisposedException ->
          ()

        return finalExit
      }

  and [<AbstractClass>] BoxedEff<'env>() =
    abstract StepInto: Stepper<'env> * Frame<'env> list -> StepResult<'env>

  and [<Struct>] UnwindAction<'env> =
    | ContinueWithEff of ef: BoxedEff<'env> * effl: Frame<'env> list
    | ContinueWithExit of ex: BoxedExit * exfl: Frame<'env> list
    | RunCleanup of cln: BoxedEff<'env> * unwfn: (BoxedExit -> UnwindAction<'env>)

  and [<AbstractClass>] Frame<'env>() =
    abstract HandleOk: Stepper<'env> * obj * Frame<'env> list -> UnwindAction<'env>
    abstract HandleErr: Stepper<'env> * obj * Frame<'env> list -> UnwindAction<'env>
    abstract HandleExn: Stepper<'env> * exn * Frame<'env> list -> UnwindAction<'env>
    abstract HandleAborted: Stepper<'env> * Frame<'env> list -> UnwindAction<'env>
    abstract IsCleanupBoundary: bool

  and Stepper<'env> =
    abstract Env: 'env
    abstract Token: CancellationToken
    abstract IsCancellationMasked: bool
    abstract CurrentFiber: FiberHandle
    abstract MaskCancellation: unit -> Stepper<'env>
    abstract Project<'inner>: ('env -> 'inner) -> Stepper<'inner>
    abstract Fork: FiberHandle * CancellationToken -> Stepper<'env>
    abstract Step<'t, 'e> :
      Eff<'t, 'e, 'env> -> Frame<'env> list -> StepResult<'env>
    abstract Unwind: BoxedExit -> Frame<'env> list -> StepResult<'env>

  and INodeRuntime<'env> =
    abstract Enter: Stepper<'env> * Frame<'env> list -> StepResult<'env>

  and BoxedEff<'t, 'e, 'env>(eff: Eff<'t, 'e, 'env>) =
    inherit BoxedEff<'env>()
    member _.Eff = eff
    override _.StepInto(stepper, frames) = stepper.Step eff frames

  and TypedMachine<'env>(stepper: Stepper<'env>, initial: StepResult<'env>) =
    inherit Machine()

    let mutable current = initial

    override _.Poll() =
      let mutable result = ValueNone
      let mutable finished = false

      while not finished do
        match current with
        | Continue(nextEff, nextFrames) ->
          current <- nextEff.StepInto(stepper, nextFrames)
        | Done value ->
          result <- ValueSome(MachineDone value)
          finished <- true
        | Await(taskObj, token, cont) ->
          result <-
            ValueSome(
              MachineAwait(taskObj, token, fun taskResult ->
                TypedMachine<'env>(stepper, cont taskResult) :> Machine)
            )

          finished <- true
        | Switch(machine, cont) ->
          result <-
            ValueSome(
              MachineSwitch(machine, TypedMachineResume<'env>(stepper, cont))
            )

          finished <- true

      match result with
      | ValueSome poll -> poll
      | ValueNone -> failwith "machine poll exited without a result"

  and TypedMachineResume<'env>
    (stepper: Stepper<'env>, cont: BoxedExit -> StepResult<'env>) =
    inherit MachineResume()

    override _.Resume(exit) = TypedMachine<'env>(stepper, cont exit) :> Machine

  and MapFrame<'src, 't, 'e, 'env>(mapper: 'src -> 't) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = false

    override _.HandleOk(_, value, rest) =
      try
        ContinueWithExit(BoxedOk(box (mapper (unbox<'src> value))), rest)
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleErr(_, err, rest) = ContinueWithExit(BoxedErr err, rest)
    override _.HandleExn(_, ex, rest) = ContinueWithExit(BoxedExn ex, rest)
    override _.HandleAborted(_, rest) = ContinueWithExit(BoxedAborted, rest)

  and FlatMapFrame<'src, 't, 'e, 'env>(cont: 'src -> Eff<'t, 'e, 'env>) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = false

    override _.HandleOk(_, value, rest) =
      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(cont (unbox<'src> value)) :> BoxedEff<'env>),
          rest
        )
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleErr(_, err, rest) = ContinueWithExit(BoxedErr err, rest)
    override _.HandleExn(_, ex, rest) = ContinueWithExit(BoxedExn ex, rest)
    override _.HandleAborted(_, rest) = ContinueWithExit(BoxedAborted, rest)

  and MapErrFrame<'e1, 'e2, 'env>(mapper: 'e1 -> 'e2) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = false
    override _.HandleOk(_, value, rest) = ContinueWithExit(BoxedOk value, rest)

    override _.HandleErr(_, err, rest) =
      try
        ContinueWithExit(BoxedErr(box (mapper (unbox<'e1> err))), rest)
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleExn(_, ex, rest) = ContinueWithExit(BoxedExn ex, rest)
    override _.HandleAborted(_, rest) = ContinueWithExit(BoxedAborted, rest)

  and FlatMapErrFrame<'t, 'e, 'env>(handler: 'e -> Eff<'t, 'e, 'env>) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = false
    override _.HandleOk(_, value, rest) = ContinueWithExit(BoxedOk value, rest)

    override _.HandleErr(_, err, rest) =
      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(handler (unbox<'e> err)) :> BoxedEff<'env>),
          rest
        )
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleExn(_, ex, rest) = ContinueWithExit(BoxedExn ex, rest)
    override _.HandleAborted(_, rest) = ContinueWithExit(BoxedAborted, rest)

  and FlatMapExnFrame<'t, 'e, 'env>(handler: exn -> Eff<'t, 'e, 'env>) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = false
    override _.HandleOk(_, value, rest) = ContinueWithExit(BoxedOk value, rest)
    override _.HandleErr(_, err, rest) = ContinueWithExit(BoxedErr err, rest)

    override _.HandleExn(_, ex, rest) =
      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(handler ex) :> BoxedEff<'env>),
          rest
        )
      with handlerEx ->
        ContinueWithExit(BoxedExn handlerEx, rest)

    override _.HandleAborted(_, rest) = ContinueWithExit(BoxedAborted, rest)

  and DeferFrame<'e, 'env>(cleanup: Eff<unit, 'e, 'env>) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = true

    override _.HandleOk(_, value, rest) =
      RunCleanup(
        (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
        function
        | BoxedOk _ -> ContinueWithExit(BoxedOk value, rest)
        | BoxedErr err -> ContinueWithExit(BoxedErr err, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedAborted -> ContinueWithExit(BoxedAborted, rest)
      )

    override _.HandleErr(_, err, rest) =
      RunCleanup(
        (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
        function
        | BoxedOk _ -> ContinueWithExit(BoxedErr err, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedAborted -> ContinueWithExit(BoxedErr err, rest)
      )

    override _.HandleExn(_, ex, rest) =
      RunCleanup(
        (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
        function
        | BoxedOk _ -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
        | BoxedAborted -> ContinueWithExit(BoxedExn ex, rest)
      )

    override _.HandleAborted(_, rest) =
      RunCleanup(
        (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
        function
        | BoxedOk _ -> ContinueWithExit(BoxedAborted, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
        | BoxedAborted -> ContinueWithExit(BoxedAborted, rest)
      )

  and DeferScopeFrame<'src, 't, 'e, 'env>
    (cont: 'src -> Eff<'t, 'e, 'env>, cleanup: 'src -> Eff<unit, 'e, 'env>) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = false

    override _.HandleOk(stepper, value, rest) =
      let sourceValue = unbox<'src> value

      try
        let cleanupEff = cleanup sourceValue

        try
          ContinueWithEff(
            (BoxedEff<'t, 'e, 'env>(cont sourceValue) :> BoxedEff<'env>),
            (DeferFrame<'e, 'env>(cleanupEff) :> Frame<'env>) :: rest
          )
        with ex ->
          (DeferFrame<'e, 'env>(cleanupEff) :> Frame<'env>).HandleExn(stepper, ex, rest)
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleErr(_, err, rest) = ContinueWithExit(BoxedErr err, rest)
    override _.HandleExn(_, ex, rest) = ContinueWithExit(BoxedExn ex, rest)
    override _.HandleAborted(_, rest) = ContinueWithExit(BoxedAborted, rest)

  and BracketReleaseFrame<'r, 'e, 'env>
    (resource: 'r, release: 'r -> Eff<unit, 'e, 'env>) =
    inherit Frame<'env>()

    let runCleanup rest cont =
      try
        RunCleanup(
          (BoxedEff<unit, 'e, 'env>(release resource) :> BoxedEff<'env>),
          cont
        )
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.IsCleanupBoundary = true

    override _.HandleOk(_, value, rest) =
      runCleanup
        rest
        (function
        | BoxedOk _ -> ContinueWithExit(BoxedOk value, rest)
        | BoxedErr err -> ContinueWithExit(BoxedErr err, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedAborted -> ContinueWithExit(BoxedAborted, rest))

    override _.HandleErr(_, err, rest) =
      runCleanup
        rest
        (function
        | BoxedOk _ -> ContinueWithExit(BoxedErr err, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedAborted -> ContinueWithExit(BoxedErr err, rest))

    override _.HandleExn(_, ex, rest) =
      runCleanup
        rest
        (function
        | BoxedOk _ -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
        | BoxedAborted -> ContinueWithExit(BoxedExn ex, rest))

    override _.HandleAborted(_, rest) =
      runCleanup
        rest
        (function
        | BoxedOk _ -> ContinueWithExit(BoxedAborted, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
        | BoxedAborted -> ContinueWithExit(BoxedAborted, rest))

  and BracketAcquireFrame<'r, 't, 'e, 'env>
    (usefn: 'r -> Eff<'t, 'e, 'env>, release: 'r -> Eff<unit, 'e, 'env>) =
    inherit Frame<'env>()

    override _.IsCleanupBoundary = false

    override _.HandleOk(stepper, value, rest) =
      let resource = unbox<'r> value

      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(usefn resource) :> BoxedEff<'env>),
          (BracketReleaseFrame<'r, 'e, 'env>(resource, release) :> Frame<'env>)
          :: rest
        )
      with ex ->
        (BracketReleaseFrame<'r, 'e, 'env>(resource, release) :> Frame<'env>)
          .HandleExn(stepper, ex, rest)

    override _.HandleErr(_, err, rest) = ContinueWithExit(BoxedErr err, rest)
    override _.HandleExn(_, ex, rest) = ContinueWithExit(BoxedExn ex, rest)
    override _.HandleAborted(_, rest) = ContinueWithExit(BoxedAborted, rest)

  let private isBoundaryFrames (frames: Frame<'env> list) =
    frames |> List.forall (fun frame -> frame.IsCleanupBoundary)

  let rec unwind
    (stepper: Stepper<'env>)
    (exit: BoxedExit)
    (frames: Frame<'env> list)
    : StepResult<'env> =
    let mutable currentExit = exit
    let mutable currentFrames = frames
    let mutable result = ValueNone
    let mutable finished = false

    while not finished do
      if isBoundaryFrames currentFrames && stepper.CurrentFiber.TryBeginClosing() then
        if stepper.CurrentFiber.HasLiveChildren then
          let drainTask = task {
            let! failure = stepper.CurrentFiber.DrainChildren()
            return boxOption failure
          }

          result <-
            ValueSome(
              Await(
                drainTask,
                CancellationToken.None,
                function
                | Ok boxedFailure ->
                  let nextExit =
                    match unbox<BoxedExit option> boxedFailure with
                    | Some failure -> failure
                    | None -> currentExit

                  unwind stepper nextExit currentFrames
                | Error ex -> unwind stepper (BoxedExn ex) currentFrames
              )
            )

          finished <- true

      if not finished then
        match currentFrames with
        | [] ->
          result <- ValueSome(Done currentExit)
          finished <- true
        | frame :: rest ->
          let action =
            match currentExit with
            | BoxedOk value -> frame.HandleOk(stepper, value, rest)
            | BoxedErr err -> frame.HandleErr(stepper, err, rest)
            | BoxedExn ex -> frame.HandleExn(stepper, ex, rest)
            | BoxedAborted -> frame.HandleAborted(stepper, rest)

          match action with
          | ContinueWithEff(eff, nextFrames) ->
            result <- ValueSome(Continue(eff, nextFrames))
            finished <- true
          | ContinueWithExit(nextExit, nextFrames) ->
            currentExit <- nextExit
            currentFrames <- nextFrames
          | RunCleanup(cleanup, cont) ->
            let rec continueCleanup
              (cleanupCont: BoxedExit -> UnwindAction<'env>)
              (cleanupExit: BoxedExit)
              : StepResult<'env> =
              match cleanupCont cleanupExit with
              | ContinueWithEff(eff, nextFrames) -> Continue(eff, nextFrames)
              | ContinueWithExit(nextExit, nextFrames) -> unwind stepper nextExit nextFrames
              | RunCleanup(nextCleanup, nextCont) ->
                let nestedFrame =
                  { new Frame<'env>() with
                      member _.IsCleanupBoundary = false
                      member _.HandleOk(_, value, _) = ContinueWithExit(BoxedOk value, [])
                      member _.HandleErr(_, err, _) = ContinueWithExit(BoxedErr err, [])
                      member _.HandleExn(_, ex, _) = ContinueWithExit(BoxedExn ex, [])
                      member _.HandleAborted(_, _) = ContinueWithExit(BoxedAborted, [])
                  }

                let cleanupStepper = stepper.MaskCancellation()
                let cleanupMachine =
                  TypedMachine<'env>(cleanupStepper, Continue(nextCleanup, [ nestedFrame ]))
                  :> Machine

                Switch(cleanupMachine, continueCleanup nextCont)

            let cleanupFrame =
              { new Frame<'env>() with
                  member _.IsCleanupBoundary = false
                  member _.HandleOk(_, value, _) = ContinueWithExit(BoxedOk value, [])
                  member _.HandleErr(_, err, _) = ContinueWithExit(BoxedErr err, [])
                  member _.HandleExn(_, ex, _) = ContinueWithExit(BoxedExn ex, [])
                  member _.HandleAborted(_, _) = ContinueWithExit(BoxedAborted, [])
              }

            let cleanupStepper = stepper.MaskCancellation()
            let cleanupMachine =
              TypedMachine<'env>(cleanupStepper, Continue(cleanup, [ cleanupFrame ]))
              :> Machine

            result <- ValueSome(Switch(cleanupMachine, continueCleanup cont))
            finished <- true

    match result with
    | ValueSome step -> step
    | ValueNone -> failwith "unwind loop exited without a result"

  let stepEff<'t, 'e, 'env>
    (stepper: Stepper<'env>)
    (eff: Eff<'t, 'e, 'env>)
    (frames: Frame<'env> list)
    : StepResult<'env> =
    let mutable currentEff = eff
    let mutable currentFrames = frames
    let mutable result = ValueNone
    let mutable finished = false

    while not finished do
      if stepper.Token.IsCancellationRequested && not stepper.IsCancellationMasked then
        stepper.CurrentFiber.MarkAbortObserved()
        result <- ValueSome(unwind stepper BoxedAborted currentFrames)
        finished <- true
      else
        match currentEff with
        | Eff.Pure value ->
          result <- ValueSome(unwind stepper (BoxedOk(box value)) currentFrames)
          finished <- true
        | Eff.Err err ->
          result <- ValueSome(unwind stepper (BoxedErr(box err)) currentFrames)
          finished <- true
        | Eff.Crash ex ->
          result <- ValueSome(unwind stepper (BoxedExn ex) currentFrames)
          finished <- true
        | Eff.Suspend suspend ->
          try
            currentEff <- suspend ()
          with ex ->
            result <- ValueSome(unwind stepper (BoxedExn ex) currentFrames)
            finished <- true
        | Eff.Thunk thunk ->
          try
            result <-
              ValueSome(unwind stepper (BoxedOk(box (thunk ()))) currentFrames)
          with ex ->
            result <- ValueSome(unwind stepper (BoxedExn ex) currentFrames)

          finished <- true
        | Eff.Task tsk ->
          try
            let awaitToken =
              if stepper.IsCancellationMasked then
                CancellationToken.None
              else
                stepper.Token

            let awaited = task {
              let! value = tsk ()
              return box value
            }

            result <-
              ValueSome(
                Await(
                  awaited,
                  awaitToken,
                  function
                  | Ok value -> unwind stepper (BoxedOk value) currentFrames
                  | Error _ when not stepper.IsCancellationMasked && stepper.Token.IsCancellationRequested ->
                    stepper.CurrentFiber.MarkAbortObserved()
                    unwind stepper BoxedAborted currentFrames
                  | Error ex -> unwind stepper (BoxedExn ex) currentFrames
                )
              )

          with ex ->
            result <- ValueSome(unwind stepper (BoxedExn ex) currentFrames)

          finished <- true
        | Eff.Read read ->
          try
            result <-
              ValueSome(unwind stepper (BoxedOk(box (read stepper.Env))) currentFrames)
          with ex ->
            result <- ValueSome(unwind stepper (BoxedExn ex) currentFrames)

          finished <- true
        | Eff.Node node ->
          try
            result <-
              ValueSome(
                match box node with
                | :? INodeRuntime<'env> as runtime -> runtime.Enter(stepper, currentFrames)
                | _ -> failwith "unknown node"
              )
          with ex ->
            result <- ValueSome(unwind stepper (BoxedExn ex) currentFrames)

          finished <- true

    match result with
    | ValueSome step -> step
    | ValueNone -> failwith "step loop exited without a result"

  let runTaskLoop (machine: Machine) : Task<BoxedExit> =
    task {
      let mutable current = machine
      let mutable resumes: MachineResume list = []
      let mutable exit = ValueNone
      let mutable finished = false

      while not finished do
        match current.Poll() with
        | MachineDone value ->
          match resumes with
          | resume :: rest ->
            resumes <- rest
            current <- resume.Resume value
          | [] ->
            exit <- ValueSome value
            finished <- true
        | MachineAwait(taskObj, token, cont) ->
          try
            let! value =
              if token.CanBeCanceled then
                taskObj.WaitAsync(token)
              else
                taskObj

            current <- cont (Ok value)
          with ex ->
            current <- cont (Error ex)
        | MachineSwitch(machine, resume) ->
          resumes <- resume :: resumes
          current <- machine

      return
        match exit with
        | ValueSome value -> value
        | ValueNone -> failwith "run loop exited without a result"
    }

  let runFiberTask (fiber: FiberHandle) (machine: Machine) : Task<BoxedExit> =
    task {
      let! exit = runTaskLoop machine
      return! fiber.Complete(exit)
    }

  type RuntimeStepper<'env>
    (
      env: 'env,
      token: CancellationToken,
      currentFiber: FiberHandle,
      cancellationMasked: bool
    ) as this =
    interface Stepper<'env> with
      member _.Env = env
      member _.Token = token
      member _.IsCancellationMasked = cancellationMasked
      member _.CurrentFiber = currentFiber

      member _.MaskCancellation() =
        if cancellationMasked then
          this :> Stepper<'env>
        else
          RuntimeStepper<'env>(env, token, currentFiber, true) :> Stepper<'env>

      member _.Project(project: 'env -> 'inner) : Stepper<'inner> =
        RuntimeStepper<'inner>(project env, token, currentFiber, cancellationMasked)
        :> Stepper<'inner>

      member _.Fork(childFiber: FiberHandle, childToken: CancellationToken) : Stepper<'env> =
        RuntimeStepper<'env>(env, childToken, childFiber, cancellationMasked)
        :> Stepper<'env>

      member _.Step inner frames = stepEff (this :> Stepper<'env>) inner frames
      member _.Unwind exit frames = unwind (this :> Stepper<'env>) exit frames
