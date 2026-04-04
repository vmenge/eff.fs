namespace EffFs.Core

open System.Threading.Tasks

[<RequireQualifiedAccess>]
type Source<'env> =
    private
    | Task of (unit -> Task<obj>)
    | Read of ('env -> obj)

[<Struct>]
[<RequireQualifiedAccess>]
type Eff<'t, 'e, 'env> =
    | Pure of value: 't
    | Err of err: 'e
    | Delay of delay: (unit -> Eff<'t, 'e, 'env>)
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

exception ValueIsNone

module Eff =
    type t<'t> = Eff<'t, unit, unit>
    let inline ask () : Eff<'a, 'e, 'a> = Eff.Read id
    let inline read (f: 'a -> 'b) : Eff<'b, 'e, 'a> = Eff.Read f
    let value t = Eff.Pure t
    let err e = Eff.Err e
    let errwith msg = Eff.Err(msg)
    let delay f = Eff.Delay f
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
        | None -> Eff.Err(ValueIsNone)

    let ofOptionWith f o =
        match o with
        | Some v -> Eff.Pure v
        | None -> Eff.Err(f ())

    let ofValueOption o =
        match o with
        | ValueSome v -> Eff.Pure v
        | ValueNone -> Eff.Err(ValueIsNone)

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

    let mapErr f ef =
        match ef with
        | Eff.Err e -> Eff.Err(f e)
        | _ -> ef

    let rec map f ef =
        match ef with
        | Eff.Pure v -> Eff.Pure(f v)
        | Eff.Err err -> Eff.Err err
        | Eff.Delay delay -> Eff.Delay(fun () -> map f (delay ()))
        | Eff.Thunk thunk -> Eff.Thunk(fun () -> f (thunk ()))
        | Eff.Task t ->
            Eff.Task(fun () -> task {
                let! x = t ()
                return f x
            })

        | Eff.Read read -> Eff.Read(fun env -> f (read env))
        | Eff.Pending pending ->
            match pending with
            | Map(source, mapper) ->
                Eff.Pending(Map(source, fun x -> f (mapper x)))
            | FlatMap(source, cont) ->
                Eff.Pending(FlatMap(source, fun x -> cont x |> map f))
            | FlatMapSource(source, cont) ->
                Eff.Pending(FlatMapSource(source, fun x -> cont x |> map f))
            | Defer(body, cleanup) -> Eff.Pending(Defer(map f body, cleanup))


    let rec bind f ef =
        match ef with
        | Eff.Pure v -> f v
        | Eff.Err err -> Eff.Err err
        | Eff.Delay delay -> Eff.Delay(fun () -> bind f (delay ()))
        | Eff.Thunk thunk -> Eff.Delay(fun () -> f (thunk ()))

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
        Eff.Delay(fun () ->
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

                | Eff.Delay delay ->
                    try
                        current <- delay ()
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

[<AutoOpen>]
module CE =
    type EffBuilder() =
        member _.Yield(value: 't) : Eff<'t, 'e, 'env> = Eff.value value

        member _.Return(value: 't) : Eff<'t, 'e, 'env> = Eff.value value

        member _.ReturnFrom(eff: Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> = eff

        member _.Bind
            (eff: Eff<'t, 'e, 'env>, f: 't -> Eff<'u, 'e, 'env>)
            : Eff<'u, 'e, 'env> =
            Eff.bind f eff

        member _.BindReturn
            (eff: Eff<'t, 'e, 'env>, f: 't -> 'u)
            : Eff<'u, 'e, 'env> =
            Eff.map f eff

        member _.Zero() : Eff<unit, 'e, 'env> = Eff.value ()

        member _.Delay(f: unit -> Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> =
            Eff.delay f

        member _.Combine
            (left: Eff<unit, 'e, 'env>, right: Eff<'t, 'e, 'env>)
            : Eff<'t, 'e, 'env> =
            Eff.bind (fun () -> right) left

        member _.Using
            (resource: 'r, binder: 'r -> Eff<'t, 'e, 'env>)
            : Eff<'t, 'e, 'env> when 'r :> System.IDisposable =
            Eff.bracket
                (Eff.value resource)
                (fun r -> Eff.thunk (fun () -> r.Dispose()))
                binder

        member this.While
            (guard: unit -> bool, body: Eff<unit, 'e, 'env>)
            : Eff<unit, 'e, 'env> =
            if not (guard ()) then
                Eff.value ()
            else
                Eff.bind (fun () -> this.While(guard, body)) body

        member _.For
            (sequence: seq<'t>, body: 't -> Eff<unit, 'e, 'env>)
            : Eff<unit, 'e, 'env> =
            use enumerator = sequence.GetEnumerator()

            let rec loop () =
                if enumerator.MoveNext() then
                    Eff.bind (fun () -> loop ()) (body enumerator.Current)
                else
                    Eff.value ()

            loop ()

        [<CustomOperation("defer", MaintainsVariableSpaceUsingBind = true)>]
        member _.Defer
            (
                state: Eff<'t, 'e, 'env>,
                [<ProjectionParameter>] cleanup: 't -> Eff<unit, 'e, 'env>
            ) : Eff<'t, 'e, 'env> =
            state
            |> Eff.bind (fun vspace ->
                Eff.defer (cleanup vspace) (Eff.value vspace)
            )

        [<CustomOperation("defer", MaintainsVariableSpaceUsingBind = true)>]
        member _.Defer
            (
                state: Eff<'t, 'e, 'env>,
                [<ProjectionParameter>] cleanup: 't -> unit -> unit
            ) : Eff<'t, 'e, 'env> =
            state
            |> Eff.bind (fun vspace ->
                Eff.defer (Eff.thunk (cleanup vspace)) (Eff.value vspace)
            )

        member _.Source(eff: Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> = eff


    [<AutoOpen>]
    module CEExtLowPriority =
        type EffBuilder with
            member _.Source(valueTask: ValueTask<'t>) : Eff<'t, 'e, 'env> =
                Eff.ofValueTask (fun () -> valueTask)

            member _.Source(task: Task<'t>) : Eff<'t, 'e, 'env> =
                Eff.ofTask (fun () -> task)

            member _.Source(async: Async<'t>) : Eff<'t, 'e, 'env> =
                Eff.ofAsync (fun () -> async)

    [<AutoOpen>]
    module CEExtHighPriority =
        type EffBuilder with
            member _.Source
                (valueTaskResult: ValueTask<Result<'t, 'e>>)
                : Eff<'t, 'e, 'env> =
                Eff.ofValueTask (fun () -> valueTaskResult)
                |> Eff.bind Eff.ofResult

            member _.Source
                (taskResult: Task<Result<'t, 'e>>)
                : Eff<'t, 'e, 'env> =
                Eff.ofTask (fun () -> taskResult) |> Eff.bind Eff.ofResult

            member _.Source
                (asyncResult: Async<Result<'t, 'e>>)
                : Eff<'t, 'e, 'env> =
                Eff.ofAsync (fun () -> asyncResult) |> Eff.bind Eff.ofResult

            member _.Source(result: Result<'t, 'e>) : Eff<'t, 'e, 'env> =
                Eff.ofResult result

            member _.Source(option: Option<'t>) : Eff<'t, exn, 'env> =
                Eff.ofOption option

            member _.Source(valueOption: ValueOption<'t>) : Eff<'t, exn, 'env> =
                Eff.ofValueOption valueOption

    let eff = EffBuilder()
