namespace EffFs.Core

open System.Threading.Tasks

module ReportCE =
    type EffBuilder() =
        inherit CE.EffBuilderBase()

        member _.ReturnFrom(eff: Eff<'t, 'e, 'env>) : Eff<'t, exn, 'env> =
            eff |> Eff.toReport

        member _.Bind
            (eff: Eff<'t, 'e, 'env>, f: 't -> Eff<'u, 'e, 'env>)
            : Eff<'u, exn, 'env> =
            Eff.bind f eff |> Eff.toReport

        member _.BindReturn
            (eff: Eff<'t, 'e, 'env>, f: 't -> 'u)
            : Eff<'u, exn, 'env> =
            Eff.map f eff |> Eff.toReport

        member _.Source(eff: Eff<'t, 'e, 'env>) : Eff<'t, exn, 'env> =
            eff |> Eff.toReport

    [<AutoOpen>]
    module CEExtLowPriority =
        type EffBuilder with
            member _.Source(valueTask: ValueTask<'t>) : Eff<'t, exn, 'env> =
                Eff.ofValueTask (fun () -> valueTask)

            member _.Source(task: Task<'t>) : Eff<'t, exn, 'env> =
                Eff.ofTask (fun () -> task)

            member _.Source(async: Async<'t>) : Eff<'t, exn, 'env> =
                Eff.ofAsync (fun () -> async)

    [<AutoOpen>]
    module CEExtHighPriority =
        type EffBuilder with
            member _.Source
                (valueTaskResult: ValueTask<Result<'t, 'e>>)
                : Eff<'t, exn, 'env> =
                Eff.ofValueTask (fun () -> valueTaskResult)
                |> Eff.bind (Eff.ofResult >> Eff.toReport)

            member _.Source
                (taskResult: Task<Result<'t, 'e>>)
                : Eff<'t, exn, 'env> =
                Eff.ofTask (fun () -> taskResult)
                |> Eff.bind Eff.ofResult
                |> Eff.toReport

            member _.Source
                (asyncResult: Async<Result<'t, 'e>>)
                : Eff<'t, exn, 'env> =
                Eff.ofAsync (fun () -> asyncResult)
                |> Eff.bind Eff.ofResult
                |> Eff.toReport

            member _.Source(result: Result<'t, 'e>) : Eff<'t, exn, 'env> =
                Eff.ofResult result |> Eff.toReport

            member _.Source(option: Option<'t>) : Eff<'t, exn, 'env> =
                Eff.ofOption option

            member _.Source(valueOption: ValueOption<'t>) : Eff<'t, exn, 'env> =
                Eff.ofValueOption valueOption

    let eff = EffBuilder()
