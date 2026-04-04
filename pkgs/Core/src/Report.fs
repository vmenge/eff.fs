namespace EffFs.Core

type Report(o: obj, msg: string, ?inner: exn) =
    inherit System.Exception(msg, defaultArg inner null)

    member _.Err = o

module Report =
    let makewith msg (o: obj) =
        match o with
        | :? Report as x -> x :> exn
        | :? exn as x -> Report(o, msg, x)
        | _ -> Report(o, msg) :> exn

    let make (o: obj) =
        match o with
        | :? Report as x -> x :> exn
        | :? exn as x -> Report(o, x.Message, x)
        | _ -> Report(o, $"{o}") :> exn

    let (|ReportAs|_|) (ex: exn) : 'a option =
        match ex with
        | :? Report as report ->
            match report.Err with
            | :? 'a as value -> Some value
            | _ -> None
        | _ -> None
