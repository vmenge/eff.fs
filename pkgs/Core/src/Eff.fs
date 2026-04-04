namespace EffFs.Core

open System.Threading.Tasks

[<Struct>]
[<RequireQualifiedAccess>]
type Eff<'t, 'e, 'env> =
  | Pure of value: 't
  | Err of err: 'e
  | Suspend of suspend: (unit -> Eff<'t, 'e, 'env>)
  | Thunk of thunk: (unit -> 't)
  | Task of tsk: (unit -> Task<'t>)
  | Read of read: ('env -> 't)
  | Pending of Pending<'t, 'e, 'env>

and Pending<'t, 'e, 'env> =
  private
  | Map of source: Eff<obj, 'e, 'env> * map: (obj -> 't)
  | FlatMap of source: Eff<obj, 'e, 'env> * cont: (obj -> Eff<'t, 'e, 'env>)
  | FlatMapSource of source: Source<'env> * cont: (obj -> Eff<'t, 'e, 'env>)
  | Defer of body: Eff<'t, 'e, 'env> * cleanup: Eff<unit, 'e, 'env>

and [<RequireQualifiedAccess; Struct>] Source<'env> =
  private
  | Task of tsk: (unit -> Task<obj>)
  | Read of read: ('env -> obj)

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
  let value t = Eff.Pure t
  let err e = Eff.Err e
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

  let rec private mapErrObj
    (f: 'e1 -> 'e2)
    (ef: Eff<obj, 'e1, 'env>)
    : Eff<obj, 'e2, 'env> =
    match ef with
    | Eff.Pure v -> Eff.Pure v
    | Eff.Err e -> Eff.Err(f e)
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> mapErrObj f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk thunk
    | Eff.Task tsk -> Eff.Task tsk
    | Eff.Read read -> Eff.Read read
    | Eff.Pending pending ->
      match pending with
      | Map(source, mapper) -> Eff.Pending(Map(mapErrObj f source, mapper))
      | FlatMap(source, cont) ->
        Eff.Pending(FlatMap(mapErrObj f source, fun x -> mapErrObj f (cont x)))
      | FlatMapSource(source, cont) ->
        Eff.Pending(FlatMapSource(source, fun x -> mapErrObj f (cont x)))
      | Defer(body, cleanup) ->
        Eff.Pending(Defer(mapErrObj f body, mapErrUnit f cleanup))

  and private mapErrUnit
    (f: 'e1 -> 'e2)
    (ef: Eff<unit, 'e1, 'env>)
    : Eff<unit, 'e2, 'env> =
    match ef with
    | Eff.Pure v -> Eff.Pure v
    | Eff.Err e -> Eff.Err(f e)
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> mapErrUnit f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk thunk
    | Eff.Task tsk -> Eff.Task tsk
    | Eff.Read read -> Eff.Read read
    | Eff.Pending pending ->
      match pending with
      | Map(source, mapper) -> Eff.Pending(Map(mapErrObj f source, mapper))
      | FlatMap(source, cont) ->
        Eff.Pending(FlatMap(mapErrObj f source, fun x -> mapErrUnit f (cont x)))
      | FlatMapSource(source, cont) ->
        Eff.Pending(FlatMapSource(source, fun x -> mapErrUnit f (cont x)))
      | Defer(body, cleanup) ->
        Eff.Pending(Defer(mapErrUnit f body, mapErrUnit f cleanup))

  and mapErr (f: 'e1 -> 'e2) (ef: Eff<'t, 'e1, 'env>) : Eff<'t, 'e2, 'env> =
    match ef with
    | Eff.Pure v -> Eff.Pure v
    | Eff.Err e -> Eff.Err(f e)
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> mapErr f (suspend ()))
    | Eff.Thunk thunk -> Eff.Thunk thunk
    | Eff.Task tsk -> Eff.Task tsk
    | Eff.Read read -> Eff.Read read
    | Eff.Pending pending ->
      match pending with
      | Map(source, mapper) -> Eff.Pending(Map(mapErrObj f source, mapper))
      | FlatMap(source, cont) ->
        Eff.Pending(FlatMap(mapErrObj f source, fun x -> mapErr f (cont x)))
      | FlatMapSource(source, cont) ->
        Eff.Pending(FlatMapSource(source, fun x -> mapErr f (cont x)))
      | Defer(body, cleanup) ->
        Eff.Pending(Defer(mapErr f body, mapErrUnit f cleanup))

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
    | Eff.Pending pending ->
      match pending with
      | Map(source, mapper) -> Eff.Pending(Map(source, fun x -> f (mapper x)))
      | FlatMap(source, cont) ->
        Eff.Pending(FlatMap(source, fun x -> cont x |> map f))
      | FlatMapSource(source, cont) ->
        Eff.Pending(FlatMapSource(source, fun x -> cont x |> map f))
      | Defer(body, cleanup) -> Eff.Pending(Defer(map f body, cleanup))


  let rec bind f ef =
    match ef with
    | Eff.Pure v -> f v
    | Eff.Err err -> Eff.Err err
    | Eff.Suspend suspend -> Eff.Suspend(fun () -> bind f (suspend ()))
    | Eff.Thunk thunk -> Eff.Suspend(fun () -> f (thunk ()))

    | Eff.Task tsk ->
      let source =
        Source.Task(fun () -> task {
          let! x = tsk ()
          return box x
        })

      Eff.Pending(FlatMapSource(source, fun x -> f (unbox x)))

    | Eff.Read read ->
      let source = Source.Read(fun env -> box (read env))
      Eff.Pending(FlatMapSource(source, fun x -> f (unbox<'t> x)))

    | Eff.Pending pending ->
      match pending with
      | Map(source, mapper) ->
        Eff.Pending(FlatMap(source, fun x -> mapper x |> f))
      | FlatMap(source, cont) ->
        Eff.Pending(FlatMap(source, fun x -> bind f (cont x)))
      | FlatMapSource(source, cont) ->
        Eff.Pending(FlatMapSource(source, fun x -> bind f (cont x)))
      | Defer(body, cleanup) -> Eff.Pending(Defer(bind f body, cleanup))

  let defer cleanup body = Eff.Pending(Defer(body, cleanup))

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

  let rec private runLoop<'t, 'e, 'env>
    (env: 'env)
    (eff: Eff<'t, 'e, 'env>)
    : Task<Exit<'t, 'e>> =
    task {
      let mutable current = eff
      let mutable finished = false
      let mutable result: Exit<'t, 'e> = Exit.Exn(exn "unreachable")

      while not finished do
        match current with
        | Eff.Pure value ->
          result <- Exit.Ok value
          finished <- true

        | Eff.Err err ->
          result <- Exit.Err err
          finished <- true

        | Eff.Suspend suspend ->
          try
            current <- suspend ()
          with e ->
            result <- Exit.Exn e
            finished <- true

        | Eff.Thunk thunk ->
          try
            current <- Eff.Pure(thunk ())
          with e ->
            result <- Exit.Exn e
            finished <- true

        | Eff.Task tsk ->
          try
            let! value = tsk ()
            current <- Eff.Pure value
          with e ->
            result <- Exit.Exn e
            finished <- true

        | Eff.Read read ->
          try
            current <- Eff.Pure(read env)
          with e ->
            result <- Exit.Exn e
            finished <- true

        | Eff.Pending pending ->
          match pending with
          | FlatMapSource(source, cont) ->
            try
              match source with
              | Source.Task source ->
                let! x = source ()
                current <- cont x
              | Source.Read read -> current <- cont (read env)
            with e ->
              result <- Exit.Exn e
              finished <- true

          | Map(source, mapf) -> current <- map mapf source

          | FlatMap(source, cont) -> current <- bind cont source

          | Defer(body, cleanup) ->
            let! bodyResult = runLoop env body
            let! cleanupResult = runLoop env cleanup

            match cleanupResult with
            | Exit.Err e -> current <- Eff.Err e
            | Exit.Exn e ->
              result <- Exit.Exn e
              finished <- true

            | Exit.Ok() ->
              match bodyResult with
              | Exit.Ok value -> current <- Eff.Pure value
              | Exit.Err e -> current <- Eff.Err e
              | Exit.Exn e ->
                result <- Exit.Exn e
                finished <- true

      return result
    }

  let runTask (env: 'env) (eff: Eff<'t, 'e, 'env>) : Task<Exit<'t, 'e>> =
    runLoop env eff

  let runSync (env: 'env) (eff: Eff<'t, 'e, 'env>) : Exit<'t, 'e> =
    runTask env eff |> _.GetAwaiter().GetResult()
