namespace EffSharp.Core

open System
open System.Threading.Tasks

module Eff =
  type t<'t> = Eff<'t, unit, unit>
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
    | Eff.Err e ->
      Eff.Suspend(fun () ->
        Eff.Err(f e)
      )
    | Eff.Crash ex -> Eff.Crash ex
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> mapErr f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk thunk
    | Eff.Task tsk -> Eff.Task tsk
    | Eff.Read read -> Eff.Read read
    | Eff.Node _ -> Eff.Node(EffNodes.MapErr(ef, f))

  let rec map f ef =
    match ef with
    | Eff.Pure v ->
      Eff.Suspend(fun () ->
        Eff.Pure(f v)
      )
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
    | Eff.Pure v ->
      Eff.Suspend(fun () ->
        f v
      )
    | Eff.Err err -> Eff.Err err
    | Eff.Crash ex -> Eff.Crash ex
    | Eff.Suspend _ -> Eff.Node(EffNodes.FlatMap(ef, f))
    | Eff.Thunk thunk -> Eff.Suspend(fun () -> f (thunk ()))
    | Eff.Task _ -> Eff.Node(EffNodes.FlatMap(ef, f))
    | Eff.Read _ -> Eff.Node(EffNodes.FlatMap(ef, f))
    | Eff.Node node ->
      match box node with
      | :? EffNodes.IDeferScopeOps<'t, 'e, 'env> as n ->
        n.RebuildScope(fun inner -> bind f inner)
      | _ -> Eff.Node(EffNodes.FlatMap(ef, f))

  let rec internal bindReturn f ef =
    match ef with
    | Eff.Node node ->
      match box node with
      | :? EffNodes.IDeferScopeOps<'t, 'e, 'env> as n ->
        n.RebuildScope(fun inner -> bindReturn f inner)
      | _ -> map f ef
    | _ -> map f ef

  let rec internal deferScope cleanup eff =
    match eff with
    | Eff.Node node ->
      match box node with
      | :? EffNodes.IDeferScopeOps<'t, 'e, 'env> as n ->
        n.RebuildScope(fun inner -> deferScope cleanup inner)
      | _ ->
        Eff.Node(EffNodes.DeferScope<'t, 't, 'e, 'env>(eff, Eff.Pure, cleanup))
    | _ ->
      Eff.Node(EffNodes.DeferScope<'t, 't, 'e, 'env>(eff, Eff.Pure, cleanup))

  let internal closeScope eff =
    match eff with
    | Eff.Node node ->
      match box node with
      | :? EffNodes.IDeferScopeNode -> Eff.Suspend(fun () -> eff)
      | _ -> eff
    | _ -> eff

  let ensure cleanup body =
    Eff.Node(EffNodes.Ensure(body, cleanup |> map ignore))

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
    catch (fun ex -> f ex |> bind (fun _ -> Eff.Crash ex)) eff

  let orRaise eff : Eff<_, unit, _> =
    eff |> orElseWith (fun e -> Eff.Thunk(fun () -> raise (Report.make e)))

  let orRaiseWith f eff : Eff<_, unit, _> =
    eff |> orElseWith (fun e -> Eff.Thunk(fun () -> raise (f e)))

  let fork (eff: Eff<'t, 'e, 'env>) : Eff<Fiber<'t, 'e>, 'e, 'env> =
    Eff.Node(EffNodes.Fork(eff, false))

  let forkOn (eff: Eff<'t, 'e, 'env>) : Eff<Fiber<'t, 'e>, 'e, 'env> =
    Eff.Node(EffNodes.Fork(eff, true))

  let race (left: Eff<'t, 'e, 'env>) (right: Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> =
    Eff.Node(EffNodes.Race(left, right))

  let all (effects: Eff<'t, 'e, 'env> list) : Eff<'t list, 'e, 'env> =
    Eff.Node(EffNodes.All(effects))

  let timeout (duration: TimeSpan) (eff: Eff<'t, 'e, 'env>) : Eff<TimeoutResult<'t>, 'e, 'env> =
    Eff.Node(EffNodes.Timeout(duration, eff))

  let runTask (env: 'env) (eff: Eff<'t, 'e, 'env>) : Task<Exit<'t, 'e>> =
    let rootFiber = EffRuntime.FiberHandle(None, new System.Threading.CancellationTokenSource())
    let stepper =
      EffRuntime.RuntimeStepper<'env>(env, rootFiber.Token, rootFiber, false)
      :> EffRuntime.Stepper<'env>
    let machine =
      EffRuntime.TypedMachine<'env>(stepper, stepper.Step eff []) :> EffRuntime.Machine

    task {
      let! exit = EffRuntime.runFiberTask rootFiber machine

      match exit with
      | EffRuntime.BoxedOk value -> return Exit.Ok(unbox<'t> value)
      | EffRuntime.BoxedErr err -> return Exit.Err(unbox<'e> err)
      | EffRuntime.BoxedAborted -> return Exit.Aborted
      | EffRuntime.BoxedExn ex -> return Exit.Exn ex
    }

  let runSync (env: 'env) (eff: Eff<'t, 'e, 'env>) : Exit<'t, 'e> =
    runTask env eff |> _.GetAwaiter().GetResult()

module Fiber =
  let await (fiber: Fiber<'t, 'e>) : Eff<Exit<'t, 'e>, unit, 'env> =
    Eff.Node(EffNodes.AwaitFiber(fiber))

  let join (fiber: Fiber<'t, 'e>) : Eff<'t, 'e, 'env> =
    Eff.Node(EffNodes.JoinFiber(fiber))

  let abort (fiber: Fiber<'t, 'e>) : Eff<unit, 'e, 'env> =
    Eff.Node(EffNodes.AbortFiber(fiber))
