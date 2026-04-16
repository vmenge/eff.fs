namespace EffSharp.Core

module internal EffNodes =
  open System
  open System.Collections.Generic
  open System.Threading
  open System.Threading.Tasks

  type IDeferScopeNode = interface end

  type IDeferScopeOps<'t, 'e, 'env> =
    abstract RebuildScope<'u> : (Eff<'t, 'e, 'env> -> Eff<'u, 'e, 'env>) -> Eff<'u, 'e, 'env>

  let private isFailure exit =
    match exit with
    | EffRuntime.BoxedErr _
    | EffRuntime.BoxedExn _ -> true
    | EffRuntime.BoxedOk _
    | EffRuntime.BoxedAborted -> false

  let private isAbortCleanupFailure
    (handle: EffRuntime.FiberHandle)
    (exit: EffRuntime.BoxedExit)
    =
    handle.CompletionClassification = EffRuntime.CompletionClassification.AbortCleanupFailure
    && isFailure exit

  let private observeCompletion
    (inbox: EffRuntime.CompletionInbox<'t>)
    (itemFactory: EffRuntime.FiberHandle -> EffRuntime.BoxedExit -> 't)
    (handle: EffRuntime.FiberHandle)
    =
    handle.CompletionTask.ContinueWith(
      Action<Task<EffRuntime.BoxedExit>>(fun completed ->
        inbox.Publish(itemFactory handle completed.Result)
      ),
      TaskContinuationOptions.ExecuteSynchronously
    )
    |> ignore

  let private startChild
    (stepper: EffRuntime.Stepper<'env>)
    (body: Eff<'t, 'e, 'env>)
    (useTaskRun: bool)
    : EffRuntime.FiberHandle option =
    let cts = new CancellationTokenSource()
    let childHandle = EffRuntime.FiberHandle(Some stepper.CurrentFiber, cts)

    if not (stepper.CurrentFiber.TryRegisterChild childHandle) then
      cts.Dispose()
      None
    else
      let childStepper = stepper.Fork(childHandle, childHandle.Token)
      let childMachine =
        EffRuntime.TypedMachine<'env>(childStepper, childStepper.Step body [])
        :> EffRuntime.Machine

      let childTask =
        if useTaskRun then
          Task.Run<EffRuntime.BoxedExit>(Func<Task<EffRuntime.BoxedExit>>(fun () ->
            EffRuntime.runFiberTask childHandle childMachine
          ))
        else
          EffRuntime.runFiberTask childHandle childMachine

      childHandle.AttachTask(childTask)
      Some childHandle

  let private abortOutstanding (handles: EffRuntime.FiberHandle seq) =
    task {
      let handles = handles |> Seq.toArray
      let mutable firstFailure = None
      let inbox = new EffRuntime.CompletionInbox<struct (EffRuntime.FiberHandle * EffRuntime.BoxedExit)>()

      try
        for handle in handles do
          observeCompletion inbox (fun handle exit -> struct (handle, exit)) handle
          handle.RequestAbort()

        for _ in handles do
          let! struct (handle, exit) = inbox.Receive()

          if Option.isNone firstFailure && isAbortCleanupFailure handle exit then
            firstFailure <- Some exit

        return firstFailure
      finally
        (inbox :> IDisposable).Dispose()
    }

  type Map<'src, 't, 'e, 'env>
    (source: Eff<'src, 'e, 'env>, mapper: 'src -> 't) =
    inherit Node<'t, 'e, 'env>()
    member _.Source = source
    member _.Mapper = mapper

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'src, 'e, 'env>(source) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.MapFrame<'src, 't, 'e, 'env>(mapper) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type FlatMap<'src, 't, 'e, 'env>
    (source: Eff<'src, 'e, 'env>, cont: 'src -> Eff<'t, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()
    member _.Source = source
    member _.Cont = cont

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'src, 'e, 'env>(source) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.FlatMapFrame<'src, 't, 'e, 'env>(cont) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type MapErr<'t, 'e1, 'e2, 'env>
    (body: Eff<'t, 'e1, 'env>, mapper: 'e1 -> 'e2) =
    inherit Node<'t, 'e2, 'env>()
    member _.Body = body
    member _.Mapper = mapper

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e1, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.MapErrFrame<'e1, 'e2, 'env>(mapper) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type FlatMapErr<'t, 'e, 'env>
    (body: Eff<'t, 'e, 'env>, handler: 'e -> Eff<'t, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()
    member _.Body = body
    member _.Handler = handler

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.FlatMapErrFrame<'t, 'e, 'env>(handler) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type FlatMapExn<'t, 'e, 'env>
    (body: Eff<'t, 'e, 'env>, handler: exn -> Eff<'t, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.FlatMapExnFrame<'t, 'e, 'env>(handler) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type DeferScope<'src, 't, 'e, 'env>
    (
      source: Eff<'src, 'e, 'env>,
      cont: 'src -> Eff<'t, 'e, 'env>,
      cleanup: 'src -> Eff<unit, 'e, 'env>
    ) =
    inherit Node<'t, 'e, 'env>()
    member _.Source = source
    member _.Cont = cont
    member _.Cleanup = cleanup

    interface IDeferScopeNode

    interface IDeferScopeOps<'t, 'e, 'env> with
      member _.RebuildScope<'u>(transform: Eff<'t, 'e, 'env> -> Eff<'u, 'e, 'env>) =
        Eff.Node(
          DeferScope<'src, 'u, 'e, 'env>(
            source,
            (fun sourceValue -> transform (cont sourceValue)),
            cleanup
          ))

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'src, 'e, 'env>(source) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.DeferScopeFrame<'src, 't, 'e, 'env>(cont, cleanup)
           :> EffRuntime.Frame<'env>)
          :: frames
        )

  type Ensure<'t, 'e, 'env>
    (body: Eff<'t, 'e, 'env>, cleanup: Eff<unit, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()
    member _.Body = body
    member _.Cleanup = cleanup

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'t, 'e, 'env>(body) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.DeferFrame<'e, 'env>(cleanup) :> EffRuntime.Frame<'env>)
          :: frames
        )

  type Bracket<'r, 't, 'e, 'env>
    (
      acquire: Eff<'r, 'e, 'env>,
      usefn: 'r -> Eff<'t, 'e, 'env>,
      release: 'r -> Eff<unit, 'e, 'env>
    ) =
    inherit Node<'t, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(_, frames) =
        EffRuntime.Continue(
          (EffRuntime.BoxedEff<'r, 'e, 'env>(acquire) :> EffRuntime.BoxedEff<'env>),
          (EffRuntime.BracketAcquireFrame<'r, 't, 'e, 'env>(usefn, release)
           :> EffRuntime.Frame<'env>)
          :: frames
        )

  type ProvideFrom<'t, 'e, 'envOuter, 'envInner>
    (project: 'envOuter -> 'envInner, body: Eff<'t, 'e, 'envInner>) =
    inherit Node<'t, 'e, 'envOuter>()

    interface EffRuntime.INodeRuntime<'envOuter> with
      member _.Enter(stepper, frames) =
        let childStepper = stepper.Project project
        let childMachine =
          EffRuntime.TypedMachine<'envInner>(childStepper, childStepper.Step body [])
          :> EffRuntime.Machine

        EffRuntime.Switch(childMachine, fun exit -> stepper.Unwind exit frames)

  type Fork<'t, 'e, 'env>(body: Eff<'t, 'e, 'env>, useTaskRun: bool) =
    inherit Node<Fiber<'t, 'e>, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(stepper, frames) =
        match startChild stepper body useTaskRun with
        | None ->
          stepper.Unwind EffRuntime.BoxedAborted frames
        | Some childHandle ->
          let fiber = Fiber<'t, 'e>(childHandle :> obj)
          stepper.Unwind (EffRuntime.BoxedOk(box fiber)) frames

  type AwaitFiber<'t, 'e, 'env>(fiber: Fiber<'t, 'e>) =
    inherit Node<Exit<'t, 'e>, unit, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(stepper, frames) =
        let handle = unbox<EffRuntime.FiberHandle> fiber.Handle

        let awaited = task {
          let! exit = handle.CompletionTask
          return box exit
        }

        EffRuntime.Await(
          awaited,
          CancellationToken.None,
          function
          | Ok boxedExit ->
            let observed =
              match unbox<EffRuntime.BoxedExit> boxedExit with
              | EffRuntime.BoxedOk value -> Exit.Ok(unbox<'t> value)
              | EffRuntime.BoxedErr err -> Exit.Err(unbox<'e> err)
              | EffRuntime.BoxedExn ex -> Exit.Exn ex
              | EffRuntime.BoxedAborted -> Exit.Aborted

            stepper.Unwind (EffRuntime.BoxedOk(box observed)) frames
          | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
        )

  type JoinFiber<'t, 'e, 'env>(fiber: Fiber<'t, 'e>) =
    inherit Node<'t, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(stepper, frames) =
        let handle = unbox<EffRuntime.FiberHandle> fiber.Handle

        let awaited = task {
          let! exit = handle.CompletionTask
          return box exit
        }

        EffRuntime.Await(
          awaited,
          CancellationToken.None,
          function
          | Ok boxedExit ->
            let exit = unbox<EffRuntime.BoxedExit> boxedExit
            stepper.Unwind exit frames
          | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
        )

  type AbortFiber<'t, 'e, 'env>(fiber: Fiber<'t, 'e>) =
    inherit Node<unit, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(stepper, frames) =
        let handle = unbox<EffRuntime.FiberHandle> fiber.Handle
        let alreadyCompleted = handle.CompletionTask.IsCompleted

        if not alreadyCompleted then
          handle.RequestAbort()

        let awaited = task {
          let! exit = handle.CompletionTask
          return box exit
        }

        EffRuntime.Await(
          awaited,
          CancellationToken.None,
          function
          | Ok boxedExit ->
            let exit = unbox<EffRuntime.BoxedExit> boxedExit

            if not alreadyCompleted && isAbortCleanupFailure handle exit then
              stepper.Unwind exit frames
            else
              stepper.Unwind (EffRuntime.BoxedOk(box ())) frames
          | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
        )

  type Timeout<'t, 'e, 'env>(duration: TimeSpan, body: Eff<'t, 'e, 'env>) =
    inherit Node<TimeoutResult<'t>, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(stepper, frames) =
        match startChild stepper body false with
        | None ->
          stepper.Unwind EffRuntime.BoxedAborted frames
        | Some childHandle ->
          let childTask = childHandle.CompletionTask
          let awaited = task {
            let childAsTask = childTask :> Task
            let delayTask = Task.Delay(duration)
            let! winner = Task.WhenAny(childAsTask, delayTask)

            if obj.ReferenceEquals(winner, childAsTask) then
              let! exit = childTask
              return box (true, exit)
            else
              childHandle.RequestAbort()
              let! exit = childTask
              return box (false, exit)
          }

          EffRuntime.Await(
            awaited,
            CancellationToken.None,
            function
            | Ok boxedResult ->
              let completed, exit = unbox<bool * EffRuntime.BoxedExit> boxedResult
              if completed then
                match exit with
                | EffRuntime.BoxedOk value ->
                  let result: TimeoutResult<'t> = Completed(unbox<'t> value)
                  stepper.Unwind (EffRuntime.BoxedOk(box result)) frames
                | EffRuntime.BoxedErr err ->
                  stepper.Unwind (EffRuntime.BoxedErr err) frames
                | EffRuntime.BoxedExn ex ->
                  stepper.Unwind (EffRuntime.BoxedExn ex) frames
                | EffRuntime.BoxedAborted ->
                  stepper.Unwind EffRuntime.BoxedAborted frames
              else
                match exit with
                | EffRuntime.BoxedOk _
                | EffRuntime.BoxedAborted ->
                  let result: TimeoutResult<'t> = TimedOut
                  stepper.Unwind (EffRuntime.BoxedOk(box result)) frames
                | _ when isAbortCleanupFailure childHandle exit ->
                  stepper.Unwind exit frames
                | EffRuntime.BoxedErr _
                | EffRuntime.BoxedExn _ ->
                  let result: TimeoutResult<'t> = TimedOut
                  stepper.Unwind (EffRuntime.BoxedOk(box result)) frames
            | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
          )

  type Race<'t, 'e, 'env>(left: Eff<'t, 'e, 'env>, right: Eff<'t, 'e, 'env>) =
    inherit Node<'t, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(stepper, frames) =
        match startChild stepper left false with
        | None ->
          stepper.Unwind EffRuntime.BoxedAborted frames
        | Some leftHandle ->
          match startChild stepper right false with
          | None ->
            leftHandle.RequestAbort()

            EffRuntime.Await(
              task {
                let! leftExit = leftHandle.CompletionTask
                return box leftExit
              },
              CancellationToken.None,
              function
              | Ok boxedExit ->
                let leftExit = unbox<EffRuntime.BoxedExit> boxedExit

                if isAbortCleanupFailure leftHandle leftExit then
                  stepper.Unwind leftExit frames
                else
                  stepper.Unwind EffRuntime.BoxedAborted frames
              | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
            )
          | Some rightHandle ->
            let awaited = task {
              let leftTask = leftHandle.CompletionTask :> Task
              let rightTask = rightHandle.CompletionTask :> Task
              let! completed = Task.WhenAny(leftTask, rightTask)

              let winnerHandle, loserHandle =
                if obj.ReferenceEquals(completed, leftTask) then
                  leftHandle, rightHandle
                else
                  rightHandle, leftHandle

              let! winnerExit = winnerHandle.CompletionTask
              loserHandle.RequestAbort()
              let! loserExit = loserHandle.CompletionTask
              return box (winnerExit, loserExit, loserHandle)
            }

            EffRuntime.Await(
              awaited,
              CancellationToken.None,
              function
              | Ok boxed ->
                let winnerExit, loserExit, loserHandle =
                  unbox<EffRuntime.BoxedExit * EffRuntime.BoxedExit * EffRuntime.FiberHandle> boxed

                if isAbortCleanupFailure loserHandle loserExit then
                  stepper.Unwind loserExit frames
                else
                  stepper.Unwind winnerExit frames
              | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
            )

  type All<'t, 'e, 'env>(effects: Eff<'t, 'e, 'env> list) =
    inherit Node<'t list, 'e, 'env>()

    interface EffRuntime.INodeRuntime<'env> with
      member _.Enter(stepper, frames) =
        let started = ResizeArray<int * EffRuntime.FiberHandle>()
        let mutable startFailed = false

        for index, effect in effects |> List.indexed do
          if not startFailed then
            match startChild stepper effect false with
            | Some handle -> started.Add(index, handle)
            | None -> startFailed <- true

        if startFailed then
          let startedHandles = started |> Seq.map snd

          EffRuntime.Await(
            task {
              let! cleanupFailure = abortOutstanding startedHandles
              return box cleanupFailure
            },
            CancellationToken.None,
            function
            | Ok boxedFailure ->
              match unbox<EffRuntime.BoxedExit option> boxedFailure with
              | Some failure -> stepper.Unwind failure frames
              | None -> stepper.Unwind EffRuntime.BoxedAborted frames
            | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
          )
        else
          let awaited = task {
            let results = Array.zeroCreate<obj> started.Count
            let pending = Dictionary<int64, int * EffRuntime.FiberHandle>(started.Count)
            let mutable terminalExit = None
            let inbox =
              new EffRuntime.CompletionInbox<struct (int * EffRuntime.FiberHandle * EffRuntime.BoxedExit)>()

            try
              for index, handle in started do
                pending.Add(handle.Id, (index, handle))
                observeCompletion inbox (fun handle exit -> struct (index, handle, exit)) handle

              while pending.Count > 0 && Option.isNone terminalExit do
                let! struct (resultIndex, completedHandle, exit) = inbox.Receive()

                if pending.Remove(completedHandle.Id) then
                  match exit with
                  | EffRuntime.BoxedOk value ->
                    results[resultIndex] <- value
                  | _ ->
                    terminalExit <- Some exit

              let mutable cleanupFailure = None

              if Option.isSome terminalExit then
                for KeyValue(_, (_, handle)) in pending do
                  handle.RequestAbort()

                while pending.Count > 0 && Option.isNone cleanupFailure do
                  let! struct (_, completedHandle, exit) = inbox.Receive()

                  if pending.Remove(completedHandle.Id)
                     && isAbortCleanupFailure completedHandle exit then
                    cleanupFailure <- Some exit

              return box (results, terminalExit, cleanupFailure)
            finally
              (inbox :> IDisposable).Dispose()
          }

          EffRuntime.Await(
            awaited,
            CancellationToken.None,
            function
            | Ok boxed ->
              let results, terminalExit, cleanupFailure =
                unbox<obj array * EffRuntime.BoxedExit option * EffRuntime.BoxedExit option> boxed

              match cleanupFailure with
              | Some failure -> stepper.Unwind failure frames
              | None ->
                match terminalExit with
                | Some exit -> stepper.Unwind exit frames
                | None ->
                  let values = results |> Array.toList |> List.map unbox<'t>
                  stepper.Unwind (EffRuntime.BoxedOk(box values)) frames
            | Error ex -> stepper.Unwind (EffRuntime.BoxedExn ex) frames
          )
