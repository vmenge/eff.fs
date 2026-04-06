namespace EffFs.Core

open System.Threading.Tasks

module internal EffRuntime =
  [<Struct>]
  type BoxedExit =
    | BoxedOk of ok: obj
    | BoxedErr of err: obj
    | BoxedExn of exn: exn

  [<Struct>]
  type StepResult<'env> =
    | Continue of beff: BoxedEff<'env> * fr: Frame<'env> list
    | Done of bexit: BoxedExit
    | Await of tsk: Task<obj> * awfn: (Result<obj, exn> -> StepResult<'env>)
    | Switch of machine: Machine * swfn: (BoxedExit -> StepResult<'env>)

  and [<AbstractClass>] Machine() =
    abstract Poll: unit -> MachinePoll

  and [<Struct>] MachinePoll =
    | MachineDone of bexit: BoxedExit
    | MachineAwait of tsk: Task<obj> * resfn: (Result<obj, exn> -> Machine)
    | MachineSwitch of machine: Machine * resume: MachineResume

  and [<AbstractClass>] MachineResume() =
    abstract Resume: BoxedExit -> Machine

  and [<AbstractClass>] BoxedEff<'env>() =
    abstract StepInto: Stepper<'env> * Frame<'env> list -> StepResult<'env>

  and [<Struct>] UnwindAction<'env> =
    | ContinueWithEff of ef: BoxedEff<'env> * effl: Frame<'env> list
    | ContinueWithExit of ex: BoxedExit * exfl: Frame<'env> list
    | RunCleanup of cln: BoxedEff<'env> * unwfn: (BoxedExit -> UnwindAction<'env>)

  and [<AbstractClass>] Frame<'env>() =
    abstract HandleOk: obj * Frame<'env> list -> UnwindAction<'env>
    abstract HandleErr: obj * Frame<'env> list -> UnwindAction<'env>
    abstract HandleExn: exn * Frame<'env> list -> UnwindAction<'env>

  and Stepper<'env> =
    abstract Env: 'env
    abstract Project<'inner>: ('env -> 'inner) -> Stepper<'inner>
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
        | Await(taskObj, cont) ->
          result <-
            ValueSome(
              MachineAwait(taskObj, fun taskResult ->
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

    override _.HandleOk(value, rest) =
      try
        ContinueWithExit(BoxedOk(box (mapper (unbox<'src> value))), rest)
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleErr(err, rest) = ContinueWithExit(BoxedErr err, rest)
    override _.HandleExn(ex, rest) = ContinueWithExit(BoxedExn ex, rest)

  and FlatMapFrame<'src, 't, 'e, 'env>(cont: 'src -> Eff<'t, 'e, 'env>) =
    inherit Frame<'env>()

    override _.HandleOk(value, rest) =
      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(cont (unbox<'src> value)) :> BoxedEff<'env>),
          rest
        )
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleErr(err, rest) = ContinueWithExit(BoxedErr err, rest)
    override _.HandleExn(ex, rest) = ContinueWithExit(BoxedExn ex, rest)

  and MapErrFrame<'e1, 'e2, 'env>(mapper: 'e1 -> 'e2) =
    inherit Frame<'env>()
    override _.HandleOk(value, rest) = ContinueWithExit(BoxedOk value, rest)

    override _.HandleErr(err, rest) =
      try
        ContinueWithExit(BoxedErr(box (mapper (unbox<'e1> err))), rest)
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleExn(ex, rest) = ContinueWithExit(BoxedExn ex, rest)

  and FlatMapErrFrame<'t, 'e, 'env>(handler: 'e -> Eff<'t, 'e, 'env>) =
    inherit Frame<'env>()
    override _.HandleOk(value, rest) = ContinueWithExit(BoxedOk value, rest)

    override _.HandleErr(err, rest) =
      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(handler (unbox<'e> err)) :> BoxedEff<'env>),
          rest
        )
      with ex ->
        ContinueWithExit(BoxedExn ex, rest)

    override _.HandleExn(ex, rest) = ContinueWithExit(BoxedExn ex, rest)

  and FlatMapExnFrame<'t, 'e, 'env>(handler: exn -> Eff<'t, 'e, 'env>) =
    inherit Frame<'env>()
    override _.HandleOk(value, rest) = ContinueWithExit(BoxedOk value, rest)
    override _.HandleErr(err, rest) = ContinueWithExit(BoxedErr err, rest)

    override _.HandleExn(ex, rest) =
      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(handler ex) :> BoxedEff<'env>),
          rest
        )
      with handlerEx ->
        ContinueWithExit(BoxedExn handlerEx, rest)

  and DeferFrame<'e, 'env>(cleanup: Eff<unit, 'e, 'env>) =
    inherit Frame<'env>()

    override _.HandleOk(value, rest) =
      RunCleanup(
        (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
        function
        | BoxedOk _ -> ContinueWithExit(BoxedOk value, rest)
        | BoxedErr err -> ContinueWithExit(BoxedErr err, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
      )

    override _.HandleErr(err, rest) =
      RunCleanup(
        (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
        function
        | BoxedOk _ -> ContinueWithExit(BoxedErr err, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
      )

    override _.HandleExn(ex, rest) =
      RunCleanup(
        (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
        function
        | BoxedOk _ -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
      )

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

    override _.HandleOk(value, rest) =
      runCleanup
        rest
        (function
        | BoxedOk _ -> ContinueWithExit(BoxedOk value, rest)
        | BoxedErr err -> ContinueWithExit(BoxedErr err, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
        )

    override _.HandleErr(err, rest) =
      runCleanup
        rest
        (function
        | BoxedOk _ -> ContinueWithExit(BoxedErr err, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn ex -> ContinueWithExit(BoxedExn ex, rest)
        )

    override _.HandleExn(ex, rest) =
      runCleanup
        rest
        (function
        | BoxedOk _ -> ContinueWithExit(BoxedExn ex, rest)
        | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
        | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
        )

  and BracketAcquireFrame<'r, 't, 'e, 'env>
    (usefn: 'r -> Eff<'t, 'e, 'env>, release: 'r -> Eff<unit, 'e, 'env>) =
    inherit Frame<'env>()

    override _.HandleOk(value, rest) =
      let resource = unbox<'r> value

      try
        ContinueWithEff(
          (BoxedEff<'t, 'e, 'env>(usefn resource) :> BoxedEff<'env>),
          (BracketReleaseFrame<'r, 'e, 'env>(resource, release) :> Frame<'env>)
          :: rest
        )
      with ex ->
        (BracketReleaseFrame<'r, 'e, 'env>(resource, release) :> Frame<'env>)
          .HandleExn(ex, rest)

    override _.HandleErr(err, rest) = ContinueWithExit(BoxedErr err, rest)
    override _.HandleExn(ex, rest) = ContinueWithExit(BoxedExn ex, rest)

  let unwind
    (stepper: Stepper<'env>)
    (exit: BoxedExit)
    (frames: Frame<'env> list)
    : StepResult<'env> =
    let mutable currentExit = exit
    let mutable currentFrames = frames
    let mutable result = ValueNone
    let mutable finished = false

    while not finished do
      match currentFrames with
      | [] ->
        result <- ValueSome(Done currentExit)
        finished <- true
      | frame :: rest ->
        let action =
          match currentExit with
          | BoxedOk value -> frame.HandleOk(value, rest)
          | BoxedErr err -> frame.HandleErr(err, rest)
          | BoxedExn ex -> frame.HandleExn(ex, rest)

        match action with
        | ContinueWithEff(eff, nextFrames) ->
          result <- ValueSome(Continue(eff, nextFrames))
          finished <- true
        | ContinueWithExit(nextExit, nextFrames) ->
          currentExit <- nextExit
          currentFrames <- nextFrames
        | RunCleanup(cleanup, cont) ->
          let cleanupFrame =
            { new Frame<'env>() with
                member _.HandleOk(value, _) = cont (BoxedOk value)
                member _.HandleErr(err, _) = cont (BoxedErr err)
                member _.HandleExn(ex, _) = cont (BoxedExn ex)
            }

          result <- ValueSome(Continue(cleanup, [ cleanupFrame ]))
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
          let awaited = task {
            let! value = tsk ()
            return box value
          }

          result <-
            ValueSome(
              Await(
                awaited,
                function
                | Ok value -> unwind stepper (BoxedOk value) currentFrames
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
        | MachineAwait(taskObj, cont) ->
          try
            let! value = taskObj
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

  type RuntimeStepper<'env>(env: 'env) as this =
    interface Stepper<'env> with
      member _.Env = env

      member _.Project(project: 'env -> 'inner) : Stepper<'inner> =
        RuntimeStepper<'inner>(project env) :> Stepper<'inner>

      member _.Step inner frames = stepEff (this :> Stepper<'env>) inner frames
      member _.Unwind exit frames = unwind (this :> Stepper<'env>) exit frames
