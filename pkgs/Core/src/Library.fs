namespace EffFs.Core

open System.Threading.Tasks

[<Struct>]
[<RequireQualifiedAccess>]
type Eff<'t, 'env> =
    | Pure of value: 't
    | Err of err: exn
    | Delay of delay: (unit -> Eff<'t, 'env>)
    | Thunk of thunk: (unit -> 't)
    | Task of tsk: (unit -> Task<'t>)
    | Async of asy: (unit -> Async<'t>)
    | Read of read: ('env -> 't)
    | Pending of Pending<'t, 'env>

and Pending<'t, 'env> =
    private
    | BindTask of source: (unit -> Task<obj>) * cont: (obj -> Eff<'t, 'env>)
    | BindAsync of source: (unit -> Async<obj>) * cont: (obj -> Eff<'t, 'env>)
    | BindRead of read: ('env -> obj) * cont: (obj -> Eff<'t, 'env>)
    | MapPending of source: Eff<obj, 'env> * map: (obj -> 't)
    | BindPending of source: Eff<obj, 'env> * cont: (obj -> Eff<'t, 'env>)
    | Ensure of body: Eff<'t, 'env> * cleanup: Eff<unit, 'env>

exception ValueIsNone

module Eff =
    type t<'t> = Eff<'t, unit>
    let inline ask () : Eff<'a, 'a> = Eff.Read id
    let inline read (f: 'a -> 'b) : Eff<'b, 'a> = Eff.Read f
    let value t = Eff.Pure t
    let err e = Eff.Err e
    let errwith msg = Eff.Err(exn msg)
    let delay f = Eff.Delay f
    let thunk f = Eff.Thunk f

    let ofResult (r: Result<'t, #exn>) =
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
        Eff.Task(fun () ->
            task {
                let! x = f ()
                return x
            })

    let ofAsync async = Eff.Async async

    let mapErr f ef =
        match ef with
        | Eff.Err e -> Eff.Err(f e)
        | _ -> ef

    let catch f ef =
        match ef with
        | Eff.Err e -> f e
        | _ -> ef

    let rec map f ef =
        match ef with
        | Eff.Pure v -> Eff.Pure(f v)
        | Eff.Err err -> Eff.Err err
        | Eff.Delay delay -> Eff.Delay(fun () -> map f (delay ()))
        | Eff.Thunk thunk -> Eff.Thunk(fun () -> f (thunk ()))
        | Eff.Task t ->
            Eff.Task(fun () ->
                task {
                    let! x = t ()
                    return f x
                })
        | Eff.Async asy ->
            Eff.Async(fun () ->
                async {
                    let! x = asy ()
                    return f x
                })

        | Eff.Read read -> Eff.Read(fun env -> f (read env))
        | Eff.Pending pending ->
            match pending with
            | MapPending(source, mapper) -> Eff.Pending(MapPending(source, fun x -> f (mapper x)))
            | BindTask(source, cont) -> Eff.Pending(BindTask(source, fun x -> cont x |> map f))
            | BindAsync(source, cont) -> Eff.Pending(BindAsync(source, fun x -> cont x |> map f))
            | BindRead(read, cont) -> Eff.Pending(BindRead(read, fun x -> cont x |> map f))
            | BindPending(source, cont) -> Eff.Pending(BindPending(source, fun x -> cont x |> map f))
            | Ensure(body, cleanup) -> Eff.Pending(Ensure(map f body, cleanup))


    let rec bind f ef =
        match ef with
        | Eff.Pure v -> f v
        | Eff.Err err -> Eff.Err err
        | Eff.Delay delay -> Eff.Delay(fun () -> bind f (delay ()))
        | Eff.Thunk thunk -> Eff.Delay(fun () -> f (thunk ()))

        | Eff.Task tsk ->
            let source =
                fun () ->
                    task {
                        let! x = tsk ()
                        return box x
                    }

            let cont = (fun x -> f (unbox x))
            Eff.Pending(BindTask(source, cont))

        | Eff.Async asy ->
            let source =
                (fun () ->
                    async {
                        let! x = asy ()
                        return box x
                    })

            let cont = (fun x -> f (unbox<'t> x))
            Eff.Pending(BindAsync(source, cont))

        | Eff.Read read -> Eff.Pending(BindRead((fun env -> box (read env)), (fun x -> f (unbox<'t> x))))

        | Eff.Pending pending ->
            match pending with
            | BindTask(source, cont) -> Eff.Pending(BindTask(source, fun x -> bind f (cont x)))
            | BindAsync(source, cont) -> Eff.Pending(BindAsync(source, fun x -> bind f (cont x)))
            | BindRead(read, cont) -> Eff.Pending(BindRead(read, fun x -> bind f (cont x)))
            | MapPending(source, mapper) -> Eff.Pending(BindPending(source, fun x -> mapper x |> f))
            | BindPending(source, cont) -> Eff.Pending(BindPending(source, fun x -> bind f (cont x)))
            | Ensure(body, cleanup) -> Eff.Pending(Ensure(bind f body, cleanup))

    let ensuring cleanup body = Eff.Pending(Ensure(body, cleanup))

    let bracket acquire release usefn =
        acquire |> bind (fun resource -> usefn resource |> ensuring (release resource))

    let rec private runLoop<'t, 'env> (env: 'env) (eff: Eff<'t, 'env>) : Task<Result<'t, exn>> =
        task {
            let mutable current = eff
            let mutable finished = false
            let mutable result = Error(exn "unreachable")

            while not finished do
                match current with
                | Eff.Pure value ->
                    result <- Ok value
                    finished <- true

                | Eff.Err err ->
                    result <- Error err
                    finished <- true

                | Eff.Delay delay ->
                    try
                        current <- delay ()
                    with e ->
                        current <- Eff.Err e

                | Eff.Thunk thunk ->
                    try
                        current <- Eff.Pure(thunk ())
                    with e ->
                        current <- Eff.Err e

                | Eff.Task tsk ->
                    try
                        let! value = tsk ()
                        current <- Eff.Pure value
                    with e ->
                        current <- Eff.Err e

                | Eff.Async asy ->
                    try
                        let! value = asy () |> Async.StartAsTask
                        current <- Eff.Pure value
                    with e ->
                        current <- Eff.Err e

                | Eff.Read read ->
                    try
                        current <- Eff.Pure(read env)
                    with e ->
                        current <- Eff.Err e

                | Eff.Pending pending ->
                    match pending with
                    | BindTask(source, cont) ->
                        try
                            let! x = source ()
                            current <- cont x
                        with e ->
                            current <- Eff.Err e

                    | BindAsync(source, cont) ->
                        try
                            let! x = source () |> Async.StartAsTask
                            current <- cont x
                        with e ->
                            current <- Eff.Err e

                    | BindRead(read, cont) ->
                        try
                            current <- cont (read env)
                        with e ->
                            current <- Eff.Err e

                    | MapPending(source, mapf) -> current <- map mapf source

                    | BindPending(source, cont) -> current <- bind cont source
                    | Ensure(body, cleanup) ->
                        let! bodyResult = runLoop env body
                        let! cleanupResult = runLoop env cleanup

                        match cleanupResult with
                        | Error e -> current <- Eff.Err e

                        | Ok() ->
                            match bodyResult with
                            | Ok value -> current <- Eff.Pure value
                            | Error e -> current <- Eff.Err e

            return result
        }

    let runTask (env: 'env) (eff: Eff<'t, 'env>) : Task<Result<'t, exn>> = runLoop env eff

    let runSync (env: 'env) (eff: Eff<'t, 'env>) : Result<'t, exn> =
        runTask env eff |> _.GetAwaiter().GetResult()

[<AutoOpen>]
module CE =
    type EffBuilder() =
        member _.Yield(value: 't) : Eff<'t, 'env> = Eff.value value

        member _.Return(value: 't) : Eff<'t, 'env> = Eff.value value

        member _.ReturnFrom(eff: Eff<'t, 'env>) : Eff<'t, 'env> = eff

        member _.Bind(eff: Eff<'t, 'env>, f: 't -> Eff<'u, 'env>) : Eff<'u, 'env> = Eff.bind f eff

        member _.BindReturn(eff: Eff<'t, 'env>, f: 't -> 'u) : Eff<'u, 'env> = Eff.map f eff

        member _.Zero() : Eff<unit, 'env> = Eff.value ()

        member _.Delay(f: unit -> Eff<'t, 'env>) : Eff<'t, 'env> = Eff.delay f

        member _.Combine(left: Eff<unit, 'env>, right: Eff<'t, 'env>) : Eff<'t, 'env> = Eff.bind (fun () -> right) left

        member _.TryWith(body: unit -> Eff<'t, 'env>, handler: exn -> Eff<'t, 'env>) : Eff<'t, 'env> =
            Eff.delay body |> Eff.catch handler

        member _.TryFinally(body: Eff<'t, 'env>, compensation: unit -> unit) : Eff<'t, 'env> =
            Eff.ensuring (Eff.thunk compensation) body

        member _.Using(resource: 'r, binder: 'r -> Eff<'t, 'env>) : Eff<'t, 'env> when 'r :> System.IDisposable =
            Eff.bracket (Eff.value resource) (fun r -> Eff.thunk (fun () -> r.Dispose())) binder

        member this.While(guard: unit -> bool, body: Eff<unit, 'env>) : Eff<unit, 'env> =
            if not (guard ()) then
                Eff.value ()
            else
                Eff.bind (fun () -> this.While(guard, body)) body

        member _.For(sequence: seq<'t>, body: 't -> Eff<unit, 'env>) : Eff<unit, 'env> =
            use enumerator = sequence.GetEnumerator()

            let rec loop () =
                if enumerator.MoveNext() then
                    Eff.bind (fun () -> loop ()) (body enumerator.Current)
                else
                    Eff.value ()

            loop ()

        [<CustomOperation("defer", MaintainsVariableSpaceUsingBind = true)>]
        member _.Defer(state: Eff<'t, 'env>, [<ProjectionParameter>] cleanup: 't -> Eff<unit, 'env>) : Eff<'t, 'env> =
            state
            |> Eff.bind (fun vspace -> Eff.ensuring (cleanup vspace) (Eff.value vspace))

        [<CustomOperation("defer", MaintainsVariableSpaceUsingBind = true)>]
        member _.Defer(state: Eff<'t, 'env>, [<ProjectionParameter>] cleanup: 't -> unit -> unit) : Eff<'t, 'env> =
            state
            |> Eff.bind (fun vspace -> Eff.ensuring (Eff.thunk (cleanup vspace)) (Eff.value vspace))

        member _.Source(eff: Eff<'t, 'env>) : Eff<'t, 'env> = eff


    [<AutoOpen>]
    module CEExtLowPriority =
        type EffBuilder with
            member _.Source(valueTask: ValueTask<'t>) : Eff<'t, 'env> = Eff.ofValueTask (fun () -> valueTask)

            member _.Source(task: Task<'t>) : Eff<'t, 'env> = Eff.ofTask (fun () -> task)

            member _.Source(async: Async<'t>) : Eff<'t, 'env> = Eff.ofAsync (fun () -> async)

    [<AutoOpen>]
    module CEExtHighPriority =
        type EffBuilder with
            member _.Source(valueTaskResult: ValueTask<Result<'t, exn>>) : Eff<'t, 'env> =
                Eff.ofValueTask (fun () -> valueTaskResult) |> Eff.bind Eff.ofResult

            member _.Source(taskResult: Task<Result<'t, #exn>>) : Eff<'t, 'env> =
                Eff.ofTask (fun () -> taskResult) |> Eff.bind Eff.ofResult

            member _.Source(asyncResult: Async<Result<'t, #exn>>) : Eff<'t, 'env> =
                Eff.ofAsync (fun () -> asyncResult) |> Eff.bind Eff.ofResult

            member _.Source(result: Result<'t, #exn>) : Eff<'t, 'env> = Eff.ofResult result

            member _.Source(option: Option<'t>) : Eff<'t, 'env> = Eff.ofOption option

            member _.Source(valueOption: ValueOption<'t>) : Eff<'t, 'env> = Eff.ofValueOption valueOption

    let eff = EffBuilder()
