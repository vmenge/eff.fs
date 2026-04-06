namespace EffFs.Core

open System.Threading.Tasks

module Eff =
  type t<'t> = Eff<'t, unit, unit>
  let Pure (value: 't) : Eff<'t, 'e, 'env> = Eff.Pure value
  let Err (err: 'e) : Eff<'t, 'e, 'env> = Eff.Err err
  let Crash (ex: exn) : Eff<'t, 'e, 'env> = Eff.Crash ex
  let Suspend (suspend: unit -> Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> = Eff.Suspend suspend
  let Thunk (thunk: unit -> 't) : Eff<'t, 'e, 'env> = Eff.Thunk thunk
  let Task (tsk: unit -> Task<'t>) : Eff<'t, 'e, 'env> = Eff.Task tsk
  let Read (read: 'env -> 't) : Eff<'t, 'e, 'env> = Eff.Read read
  let ask () : Eff<'a, 'e, 'a> = Eff.Read id
  let read (f: 'a -> 'b) : Eff<'b, 'e, 'a> = Eff.Read f
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
    | Eff.Crash ex -> Eff.Crash ex
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> mapErr f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk thunk
    | Eff.Task tsk -> Eff.Task tsk
    | Eff.Read read -> Eff.Read read
    | Eff.Node _ -> Eff.Node(EffNodes.MapErr(ef, f))

  let rec map f ef =
    match ef with
    | Eff.Pure v -> Eff.Pure(f v)
    | Eff.Err err -> Eff.Err err
    | Eff.Crash ex -> Eff.Crash ex
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> map f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk(fun () -> f (thunk ()))
    | Eff.Task t ->
      Eff.Task(fun () -> task {
        let! x = t ()
        return f x
      })

    | Eff.Read read -> Eff.Read(fun env -> f (read env))
    | Eff.Node _ -> Eff.Node(EffNodes.Map<_, _, _, _>(ef, f))

  let rec bind f ef =
    match ef with
    | Eff.Pure v -> f v
    | Eff.Err err -> Eff.Err err
    | Eff.Crash ex -> Eff.Crash ex
    | Eff.Suspend _ -> Eff.Node(EffNodes.FlatMap(ef, f))
    | Eff.Thunk thunk -> Eff.Suspend(fun () -> f (thunk ()))
    | Eff.Task _ -> Eff.Node(EffNodes.FlatMap(ef, f))
    | Eff.Read _ -> Eff.Node(EffNodes.FlatMap(ef, f))
    | Eff.Node node ->
      match box node with
      | :? EffNodes.Defer<'t, 'e, 'env> as n ->
        Eff.Node(EffNodes.Defer(bind f n.Body, n.Cleanup))
      | _ -> Eff.Node(EffNodes.FlatMap(ef, f))

  let defer cleanup body = Eff.Node(EffNodes.Defer(body, cleanup))

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
    Eff.Node(EffNodes.Bracket(acquire, usefn, release))

  let provideFrom project eff = Eff.Node(EffNodes.ProvideFrom(project, eff))
  let provide env eff = provideFrom (fun () -> env) eff
  let flatten eff = bind id eff

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
    Eff.Node(EffNodes.FlatMapErr(eff, handler))

  let orElse
    (fallback: Eff<'t, 'e, 'env>)
    (eff: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    orElseWith (fun _ -> fallback) eff

  let tap
    (f: 't -> Eff<'k, 'e, 'env>)
    (ef: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    ef |> bind (fun t -> f t |> map (fun _ -> t))

  let tapErr
    (f: 'e -> Eff<'k, 'e, 'env>)
    (ef: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    ef |> orElseWith (fun e -> f e |> bind (fun _ -> Eff.Err e))

  let catch
    (handler: exn -> Eff<'t, 'e, 'env>)
    (eff: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    Eff.Node(EffNodes.FlatMapExn(eff, handler))

  let tapExn
    (f: exn -> Eff<'k, 'e, 'env>)
    (eff: Eff<'t, 'e, 'env>)
    : Eff<'t, 'e, 'env> =
    catch (fun ex -> f ex |> bind (fun _ -> Crash ex)) eff

  let orRaise eff : Eff<_, unit, _> =
    eff |> orElseWith (fun e -> Thunk(fun () -> raise (Report.make e)))

  let orRaiseWith f eff : Eff<_, unit, _> =
    eff |> orElseWith (fun e -> Thunk(fun () -> raise (f e)))

  let runTask (env: 'env) (eff: Eff<'t, 'e, 'env>) : Task<Exit<'t, 'e>> =
    let stepper = EffRuntime.RuntimeStepper<'env>(env) :> EffRuntime.Stepper<'env>
    let machine =
      EffRuntime.TypedMachine<'env>(stepper, stepper.Step eff []) :> EffRuntime.Machine

    task {
      let! exit = EffRuntime.runTaskLoop machine

      match exit with
      | EffRuntime.BoxedOk value -> return Exit.Ok(unbox<'t> value)
      | EffRuntime.BoxedErr err -> return Exit.Err(unbox<'e> err)
      | EffRuntime.BoxedExn ex -> return Exit.Exn ex
    }

  let runSync (env: 'env) (eff: Eff<'t, 'e, 'env>) : Exit<'t, 'e> =
    runTask env eff |> _.GetAwaiter().GetResult()
