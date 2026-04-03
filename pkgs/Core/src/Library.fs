namespace EffFs.Core

open System.Threading.Tasks

[<Struct>]
type Eff<'t, 'env> =
    private
    | Pure of value: 't
    | Err of err: exn
    | Delay of delay: (unit -> Eff<'t, 'env>)
    | Thunk of thunk: (unit -> 't)
    | Tsk of tsk: (unit -> Task<'t>)
    | Asy of asy: (unit -> Async<'t>)
    | Read of read: ('env -> 't)
    | Pending of Pending<'t, 'env>

and private Pending<'t, 'env> =
    | BindTask of source: (unit -> Task<obj>) * cont: (obj -> Eff<'t, 'env>)
    | BindAsync of source: (unit -> Async<obj>) * cont: (obj -> Eff<'t, 'env>)
    | BindRead of read: ('env -> obj) * cont: (obj -> Eff<'t, 'env>)
    | MapPending of source: Eff<obj, 'env> * map: (obj -> 't)
    | BindPending of source: Eff<obj, 'env> * cont: (obj -> Eff<'t, 'env>)
    | Ensure of body: Eff<'t, 'env> * cleanup: Eff<unit, 'env>

exception ValueIsNone

module Eff =
    type t<'t> = Eff<'t, unit>
    let ask () = Read id
    let read f = Read f
    let value t = Pure t
    let err e = Err e
    let errwith msg = Err(exn msg)
    let delay f = Delay f
    let thunk f = Thunk f

    let ofResult (r: Result<'t, #exn>) =
        match r with
        | Ok v -> Pure v
        | Error e -> Err e

    let ofResultWith f r =
        match r with
        | Ok v -> Pure v
        | Error e -> Err(f e)

    let ofOption o =
        match o with
        | Some v -> Pure v
        | None -> Err(ValueIsNone)

    let ofOptionWith f o =
        match o with
        | Some v -> Pure v
        | None -> Err(f ())

    let ofValueOption o =
        match o with
        | ValueSome v -> Pure v
        | ValueNone -> Err(ValueIsNone)

    let ofValueOptionWith f o =
        match o with
        | ValueSome v -> Pure v
        | ValueNone -> Err(f ())

    let ofTask f = Tsk f

    let ofValueTask (f: unit -> ValueTask<'a>) =
        Tsk(fun () ->
            task {
                let! x = f ()
                return x
            })

    let ofAsync async = Asy async


    let rec map f ef =
        match ef with
        | Pure v -> Pure(f v)
        | Err err -> Err err
        | Delay delay -> Delay(fun () -> map f (delay ()))
        | Thunk thunk -> Thunk(fun () -> f (thunk ()))
        | Tsk t ->
            Tsk(fun () ->
                task {
                    let! x = t ()
                    return f x
                })
        | Asy asy ->
            Asy(fun () ->
                async {
                    let! x = asy ()
                    return f x
                })

        | Read read -> Read(fun env -> f (read env))
        | Pending pending ->
            match pending with
            | MapPending(source, mapper) -> Pending(MapPending(source, fun x -> f (mapper x)))
            | BindTask(source, cont) -> Pending(BindTask(source, fun x -> cont x |> map f))
            | BindAsync(source, cont) -> Pending(BindAsync(source, fun x -> cont x |> map f))
            | BindRead(read, cont) -> Pending(BindRead(read, fun x -> cont x |> map f))
            | BindPending(source, cont) -> Pending(BindPending(source, fun x -> cont x |> map f))
            | Ensure(body, cleanup) -> Pending(Ensure(map f body, cleanup))


    let rec bind f ef =
        match ef with
        | Pure v -> f v
        | Err err -> Err err
        | Delay delay -> Delay(fun () -> bind f (delay ()))
        | Thunk thunk -> Delay(fun () -> f (thunk ()))

        | Tsk tsk ->
            let source =
                fun () ->
                    task {
                        let! x = tsk ()
                        return box x
                    }

            let cont = (fun x -> f (unbox x))
            Pending(BindTask(source, cont))

        | Asy asy ->
            let source =
                (fun () ->
                    async {
                        let! x = asy ()
                        return box x
                    })

            let cont = (fun x -> f (unbox<'t> x))
            Pending(BindAsync(source, cont))

        | Read read -> Pending(BindRead((fun env -> box (read env)), (fun x -> f (unbox<'t> x))))

        | Pending pending ->
            match pending with
            | BindTask(source, cont) -> Pending(BindTask(source, fun x -> bind f (cont x)))
            | BindAsync(source, cont) -> Pending(BindAsync(source, fun x -> bind f (cont x)))
            | BindRead(read, cont) -> Pending(BindRead(read, fun x -> bind f (cont x)))
            | MapPending(source, mapper) -> Pending(BindPending(source, fun x -> mapper x |> f))
            | BindPending(source, cont) -> Pending(BindPending(source, fun x -> bind f (cont x)))
            | Ensure(body, cleanup) -> Pending(Ensure(bind f body, cleanup))

    let ensuring cleanup body = Pending(Ensure(body, cleanup))

    let bracket acquire release usefn =
        acquire |> bind (fun resource -> usefn resource |> ensuring (release resource))

    let rec private runLoop<'t, 'env> (env: 'env) (eff: Eff<'t, 'env>) : Task<Result<'t, exn>> =
        task {
            let mutable current = eff
            let mutable finished = false
            let mutable result = Error(exn "unreachable")

            while not finished do
                match current with
                | Pure value ->
                    result <- Ok value
                    finished <- true

                | Err err ->
                    result <- Error err
                    finished <- true

                | Delay delay ->
                    try
                        current <- delay ()
                    with e ->
                        current <- Err e

                | Thunk thunk ->
                    try
                        current <- Pure(thunk ())
                    with e ->
                        current <- Err e

                | Tsk tsk ->
                    try
                        let! value = tsk ()
                        current <- Pure value
                    with e ->
                        current <- Err e

                | Asy asy ->
                    try
                        let! value = asy () |> Async.StartAsTask
                        current <- Pure value
                    with e ->
                        current <- Err e

                | Read read ->
                    try
                        current <- Pure(read env)
                    with e ->
                        current <- Err e

                | Pending pending ->
                    match pending with
                    | BindTask(source, cont) ->
                        try
                            let! x = source ()
                            current <- cont x
                        with e ->
                            current <- Err e

                    | BindAsync(source, cont) ->
                        try
                            let! x = source () |> Async.StartAsTask
                            current <- cont x
                        with e ->
                            current <- Err e

                    | BindRead(read, cont) ->
                        try
                            current <- cont (read env)
                        with e ->
                            current <- Err e

                    | MapPending(source, mapf) -> current <- map mapf source

                    | BindPending(source, cont) -> current <- bind cont source
                    | Ensure(body, cleanup) ->
                        let! bodyResult = runLoop env body
                        let! cleanupResult = runLoop env cleanup

                        match cleanupResult with
                        | Error e -> current <- Err e

                        | Ok() ->
                            match bodyResult with
                            | Ok value -> current <- Pure value
                            | Error e -> current <- Err e

            return result
        }

    let runTask (env: 'env) (eff: Eff<'t, 'env>) : Task<Result<'t, exn>> = runLoop env eff

    let runSync (env: 'env) (eff: Eff<'t, 'env>) : Result<'t, exn> =
        runTask env eff |> _.GetAwaiter().GetResult()
