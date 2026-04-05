namespace EffFs.Core

open System.Threading.Tasks

[<Struct>]
type Eff<'t, 'e, 'env> =
  | Pure of value: 't
  | Err of err: 'e
  | Suspend of suspend: (unit -> Eff<'t, 'e, 'env>)
  | Thunk of thunk: (unit -> 't)
  | Task of tsk: (unit -> Task<'t>)
  | Read of read: ('env -> 't)
  | Node of Node<'t, 'e, 'env>

and [<Struct>] private BoxedExit =
  | BoxedOk of ok: obj
  | BoxedErr of err: obj
  | BoxedExn of exn: exn

and [<Struct>] private StepResult<'env> =
  | Continue of beff: BoxedEff<'env> * fr: Frame<'env> list
  | Done of bexit: BoxedExit
  | Await of tsk: Task<obj> * resfn: (Result<obj, exn> -> StepResult<'env>)

and [<AbstractClass>] private BoxedEff<'env>() =
  abstract StepInto: Stepper<'env> * Frame<'env> list -> StepResult<'env>

and [<Struct>] private UnwindAction<'env> =
  | ContinueWithEff of ef: BoxedEff<'env> * effl: Frame<'env> list
  | ContinueWithExit of ex: BoxedExit * exfl: Frame<'env> list
  | RunCleanup of cln: BoxedEff<'env> * unwfn: (BoxedExit -> UnwindAction<'env>)

and [<AbstractClass>] private Frame<'env>() =
  abstract HandleOk: obj * Frame<'env> list -> UnwindAction<'env>
  abstract HandleErr: obj * Frame<'env> list -> UnwindAction<'env>
  abstract HandleExn: exn * Frame<'env> list -> UnwindAction<'env>

and private Stepper<'env> =
  abstract Step<'t, 'e> :
    Eff<'t, 'e, 'env> -> Frame<'env> list -> StepResult<'env>

and [<AbstractClass>] Node<'t, 'e, 'env>() = class end

and private INodeRuntime<'env> =
  abstract Enter: Frame<'env> list -> StepResult<'env>

and private BoxedEff<'t, 'e, 'env>(eff: Eff<'t, 'e, 'env>) =
  inherit BoxedEff<'env>()
  member _.Eff = eff
  override _.StepInto(stepper, frames) = stepper.Step eff frames

and private MapFrame<'src, 't, 'e, 'env>(mapper: 'src -> 't) =
  inherit Frame<'env>()

  override _.HandleOk(value, rest) =
    try
      ContinueWithExit(BoxedOk(box (mapper (unbox<'src> value))), rest)
    with ex ->
      ContinueWithExit(BoxedExn ex, rest)

  override _.HandleErr(err, rest) = ContinueWithExit(BoxedErr err, rest)
  override _.HandleExn(ex, rest) = ContinueWithExit(BoxedExn ex, rest)

and private FlatMapFrame<'src, 't, 'e, 'env>(cont: 'src -> Eff<'t, 'e, 'env>) =
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

and private MapErrFrame<'e1, 'e2, 'env>(mapper: 'e1 -> 'e2) =
  inherit Frame<'env>()
  override _.HandleOk(value, rest) = ContinueWithExit(BoxedOk value, rest)

  override _.HandleErr(err, rest) =
    try
      ContinueWithExit(BoxedErr(box (mapper (unbox<'e1> err))), rest)
    with ex ->
      ContinueWithExit(BoxedExn ex, rest)

  override _.HandleExn(ex, rest) = ContinueWithExit(BoxedExn ex, rest)

and private FlatMapErrFrame<'t, 'e, 'env>(handler: 'e -> Eff<'t, 'e, 'env>) =
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

and private DeferFrame<'e, 'env>(cleanup: Eff<unit, 'e, 'env>) =
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

and private Map<'src, 't, 'e, 'env>
  (source: Eff<'src, 'e, 'env>, mapper: 'src -> 't) =
  inherit Node<'t, 'e, 'env>()
  member _.Source = source
  member _.Mapper = mapper

  interface INodeRuntime<'env> with
    member _.Enter(frames) =
      Continue(
        (BoxedEff<'src, 'e, 'env>(source) :> BoxedEff<'env>),
        (MapFrame<'src, 't, 'e, 'env>(mapper) :> Frame<'env>) :: frames
      )

and private FlatMap<'src, 't, 'e, 'env>
  (source: Eff<'src, 'e, 'env>, cont: 'src -> Eff<'t, 'e, 'env>) =
  inherit Node<'t, 'e, 'env>()
  member _.Source = source
  member _.Cont = cont

  interface INodeRuntime<'env> with
    member _.Enter(frames) =
      Continue(
        (BoxedEff<'src, 'e, 'env>(source) :> BoxedEff<'env>),
        (FlatMapFrame<'src, 't, 'e, 'env>(cont) :> Frame<'env>) :: frames
      )

and private MapErr<'t, 'e1, 'e2, 'env>
  (body: Eff<'t, 'e1, 'env>, mapper: 'e1 -> 'e2) =
  inherit Node<'t, 'e2, 'env>()
  member _.Body = body
  member _.Mapper = mapper

  interface INodeRuntime<'env> with
    member _.Enter(frames) =
      Continue(
        (BoxedEff<'t, 'e1, 'env>(body) :> BoxedEff<'env>),
        (MapErrFrame<'e1, 'e2, 'env>(mapper) :> Frame<'env>) :: frames
      )

and private FlatMapErr<'t, 'e, 'env>
  (body: Eff<'t, 'e, 'env>, handler: 'e -> Eff<'t, 'e, 'env>) =
  inherit Node<'t, 'e, 'env>()
  member _.Body = body
  member _.Handler = handler

  interface INodeRuntime<'env> with
    member _.Enter(frames) =
      Continue(
        (BoxedEff<'t, 'e, 'env>(body) :> BoxedEff<'env>),
        (FlatMapErrFrame<'t, 'e, 'env>(handler) :> Frame<'env>) :: frames
      )

and private Defer<'t, 'e, 'env>
  (body: Eff<'t, 'e, 'env>, cleanup: Eff<unit, 'e, 'env>) =
  inherit Node<'t, 'e, 'env>()
  member _.Body = body
  member _.Cleanup = cleanup

  interface INodeRuntime<'env> with
    member _.Enter(frames) =
      Continue(
        (BoxedEff<'t, 'e, 'env>(body) :> BoxedEff<'env>),
        (DeferFrame<'e, 'env>(cleanup) :> Frame<'env>) :: frames
      )

[<RequireQualifiedAccess>]
type Exit<'t, 'e> =
  | Ok of 't
  | Err of 'e
  | Exn of exn

module Exit =
  let isOk rr =
    match rr with
    | Exit.Ok _ -> true
    | _ -> false

  let ok rr =
    match rr with
    | Exit.Ok v -> v
    | Exit.Err e -> failwith $"{e}"
    | Exit.Exn e -> raise e

  let err rr =
    match rr with
    | Exit.Ok v -> failwith $"{v}"
    | Exit.Err e -> e
    | Exit.Exn e -> raise e

  let ex rr =
    match rr with
    | Exit.Ok v -> failwith $"{v}"
    | Exit.Err e -> failwith $"{e}"
    | Exit.Exn e -> e



module Eff =
  type t<'t> = Eff<'t, unit, unit>
  let inline ask () : Eff<'a, 'e, 'a> = Eff.Read id
  let inline read (f: 'a -> 'b) : Eff<'b, 'e, 'a> = Eff.Read f
  let failw msg = Eff.Err(exn msg)
  let report o = Eff.Err(Report.make o)
  let reportw msg o = Eff.Err(Report.makewith msg o)
  let suspend f = Eff.Suspend f
  let thunk f = Eff.Thunk f

  let ofResult (r: Result<'t, 'e>) =
    match r with
    | Ok v -> Eff.Pure v
    | Error e -> Eff.Err e

  let ofResultWith f r =
    match r with
    | Ok v -> Eff.Pure v
    | Error e -> Eff.Err(f e)

  let ofOption o =
    match o with
    | Some v -> Eff.Pure v
    | None -> report None

  let ofOptionWith f o =
    match o with
    | Some v -> Eff.Pure v
    | None -> Eff.Err(f ())

  let ofValueOption o =
    match o with
    | ValueSome v -> Eff.Pure v
    | ValueNone -> report ValueNone

  let ofValueOptionWith f o =
    match o with
    | ValueSome v -> Eff.Pure v
    | ValueNone -> Eff.Err(f ())

  let ofTask f = Eff.Task f

  let ofValueTask (f: unit -> ValueTask<'a>) =
    Eff.Task(fun () -> task {
      let! x = f ()
      return x
    })

  let ofAsync async = Eff.Task(fun () -> async () |> Async.StartAsTask)

  let rec mapErr (f: 'e1 -> 'e2) (ef: Eff<'t, 'e1, 'env>) : Eff<'t, 'e2, 'env> =
    match ef with
    | Eff.Pure v -> Eff.Pure v
    | Eff.Err e -> Eff.Err(f e)
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> mapErr f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk thunk
    | Eff.Task tsk -> Eff.Task tsk
    | Eff.Read read -> Eff.Read read
    | Eff.Node _ -> Eff.Node(MapErr(ef, f))

  let rec map f ef =
    match ef with
    | Eff.Pure v -> Eff.Pure(f v)
    | Eff.Err err -> Eff.Err err
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> map f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk(fun () -> f (thunk ()))
    | Eff.Task t ->
      Eff.Task(fun () -> task {
        let! x = t ()
        return f x
      })

    | Eff.Read read -> Eff.Read(fun env -> f (read env))
    | Eff.Node _ -> Eff.Node(Map<_, _, _, _>(ef, f))


  let rec bind f ef =
    match ef with
    | Eff.Pure v -> f v
    | Eff.Err err -> Eff.Err err
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> bind f (suspend ()))
    | Eff.Thunk thunk -> Eff.Suspend(fun () -> f (thunk ()))
    | Eff.Task _ -> Eff.Node(FlatMap(ef, f))
    | Eff.Read _ -> Eff.Node(FlatMap(ef, f))
    | Eff.Node node ->
      match box node with
      | :? Defer<'t, 'e, 'env> as n -> Eff.Node(Defer(bind f n.Body, n.Cleanup))
      | _ -> Eff.Node(FlatMap(ef, f))

  let defer cleanup body = Eff.Node(Defer(body, cleanup))

  let tryCatch (f: unit -> 't) : Eff<'t, exn, 'env> =
    Eff.Suspend(fun () ->
      try
        Eff.Pure(f ())
      with e ->
        Eff.Err e
    )

  let tryTask (f: unit -> Task<'t>) : Eff<'t, exn, 'env> =
    ofTask (fun () -> task {
      try
        let! x = f ()
        return Ok x
      with e ->
        return Error e
    })
    |> bind ofResult

  let tryAsync (f: unit -> Async<'t>) : Eff<'t, exn, 'env> =
    ofAsync (fun () -> async {
      try
        let! x = f ()
        return Ok x
      with e ->
        return Error e
    })
    |> bind ofResult

  let bracket acquire release usefn =
    acquire
    |> bind (fun resource -> usefn resource |> defer (release resource))

  let tap
    (f: 't -> Eff<'k, 'e, 'env>)
    (ef: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    ef |> bind (fun t -> f t |> map (fun _ -> t))

  let filterOr
    (pred: 't -> bool)
    (orFn: 't -> Eff<'t, 'e, 'env>)
    (ef: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    ef |> bind (fun t -> if pred t then Eff.Pure t else orFn t)

  let orElseWith
    (handler: 'e -> Eff<'t, 'e, 'env>)
    (eff: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    Eff.Node(FlatMapErr(eff, handler))

  let orElse
    (fallback: Eff<'t, 'e, 'env>)
    (eff: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    orElseWith (fun _ -> fallback) eff

  let private boxedEff (eff: Eff<'t, 'e, 'env>) : BoxedEff<'env> =
    BoxedEff<'t, 'e, 'env>(eff) :> BoxedEff<'env>

  let rec private unwind
    (stepper: Stepper<'env>)
    (exit: BoxedExit)
    (frames: Frame<'env> list)
    : StepResult<'env> =
    match frames with
    | [] -> Done exit
    | frame :: rest ->
      let action =
        match exit with
        | BoxedOk value -> frame.HandleOk(value, rest)
        | BoxedErr err -> frame.HandleErr(err, rest)
        | BoxedExn ex -> frame.HandleExn(ex, rest)

      match action with
      | ContinueWithEff(eff, nextFrames) -> Continue(eff, nextFrames)
      | ContinueWithExit(nextExit, nextFrames) ->
        unwind stepper nextExit nextFrames
      | RunCleanup(cleanup, cont) ->
        let cleanupFrame =
          { new Frame<'env>() with
              member _.HandleOk(value, _) = cont (BoxedOk value)
              member _.HandleErr(err, _) = cont (BoxedErr err)
              member _.HandleExn(ex, _) = cont (BoxedExn ex)
          }

        Continue(cleanup, [ cleanupFrame ])

  let rec private stepEff<'t, 'e, 'env>
    (stepper: Stepper<'env>)
    (env: 'env)
    (eff: Eff<'t, 'e, 'env>)
    (frames: Frame<'env> list)
    : StepResult<'env> =
    match eff with
    | Eff.Pure value -> unwind stepper (BoxedOk(box value)) frames
    | Eff.Err err -> unwind stepper (BoxedErr(box err)) frames
    | Eff.Suspend suspend ->
      try
        stepEff stepper env (suspend ()) frames
      with ex ->
        unwind stepper (BoxedExn ex) frames
    | Eff.Thunk thunk ->
      try
        unwind stepper (BoxedOk(box (thunk ()))) frames
      with ex ->
        unwind stepper (BoxedExn ex) frames
    | Eff.Task tsk ->
      try
        let awaited = tsk().ContinueWith(fun (t: Task<'t>) -> box t.Result)

        Await(
          awaited,
          function
          | Ok value -> unwind stepper (BoxedOk value) frames
          | Error ex -> unwind stepper (BoxedExn ex) frames
        )
      with ex ->
        unwind stepper (BoxedExn ex) frames
    | Eff.Read read ->
      try
        unwind stepper (BoxedOk(box (read env))) frames
      with ex ->
        unwind stepper (BoxedExn ex) frames
    | Eff.Node node ->
      match box node with
      | :? INodeRuntime<'env> as runtime -> runtime.Enter frames
      | _ -> failwith "unknown node"

  let rec private driveStep
    (env: 'env)
    (stepper: Stepper<'env>)
    (step: StepResult<'env>)
    : Task<BoxedExit> =
    task {
      match step with
      | Continue(nextEff, nextFrames) ->
        return! runTaskLoop env stepper nextEff nextFrames
      | Done exit -> return exit
      | Await(taskObj, cont) ->
        try
          let! value = taskObj
          return! driveStep env stepper (cont (Ok value))
        with ex ->
          return! driveStep env stepper (cont (Error ex))
    }

  and private runTaskLoop
    (env: 'env)
    (stepper: Stepper<'env>)
    (eff: BoxedEff<'env>)
    (frames: Frame<'env> list)
    : Task<BoxedExit> =
    driveStep env stepper (eff.StepInto(stepper, frames))

  let runTask (env: 'env) (eff: Eff<'t, 'e, 'env>) : Task<Exit<'t, 'e>> =
    let rec stepper =
      { new Stepper<'env> with
          member _.Step (inner) frames = stepEff stepper env inner frames
      }

    task {
      let! exit = runTaskLoop env stepper (boxedEff eff) []

      match exit with
      | BoxedOk value -> return Exit.Ok(unbox<'t> value)
      | BoxedErr err -> return Exit.Err(unbox<'e> err)
      | BoxedExn ex -> return Exit.Exn ex
    }

  let runSync (env: 'env) (eff: Eff<'t, 'e, 'env>) : Exit<'t, 'e> =
    runTask env eff |> _.GetAwaiter().GetResult()
