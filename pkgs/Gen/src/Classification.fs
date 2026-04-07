namespace EffSharp.Gen

open FSharp.Compiler.Symbols

module Classification =
  let private splitTopLevelArguments (text: string) =
    let rec loop index depth start acc =
      if index = text.Length then
        acc @ [ text.Substring(start).Trim() ]
      else
        match text[index] with
        | '<' -> loop (index + 1) (depth + 1) start acc
        | '>' -> loop (index + 1) (depth - 1) start acc
        | ',' when depth = 0 ->
            let part = text.Substring(start, index - start).Trim()
            loop (index + 1) depth (index + 1) (acc @ [ part ])
        | _ ->
            loop (index + 1) depth start acc

    loop 0 0 0 []

  let private tryClassifyFromRenderedText (rendered: string) =
    let classifyGeneric prefix ctor =
      if rendered.StartsWith(prefix, System.StringComparison.Ordinal)
         && rendered.EndsWith(">", System.StringComparison.Ordinal) then
        let inner = rendered.Substring(prefix.Length, rendered.Length - prefix.Length - 1)
        Some(ctor inner)
      else
        None

    let classifyTwoArg prefix ctor =
      if rendered.StartsWith(prefix, System.StringComparison.Ordinal)
         && rendered.EndsWith(">", System.StringComparison.Ordinal) then
        let inner = rendered.Substring(prefix.Length, rendered.Length - prefix.Length - 1)

        match splitTopLevelArguments inner with
        | [ a; b ] -> Some(ctor (a, b))
        | _ -> None
      else
        None

    let classifyThreeArg prefix ctor =
      if rendered.StartsWith(prefix, System.StringComparison.Ordinal)
         && rendered.EndsWith(">", System.StringComparison.Ordinal) then
        let inner = rendered.Substring(prefix.Length, rendered.Length - prefix.Length - 1)

        match splitTopLevelArguments inner with
        | [ a; b; c ] -> Some(ctor (a, b, c))
        | _ -> None
      else
        None

    classifyTwoArg "Result<" ReturnShape.Result
    |> Option.orElseWith (fun () -> classifyGeneric "System.Threading.Tasks.Task<" ReturnShape.Task)
    |> Option.orElseWith (fun () -> classifyGeneric "Async<" ReturnShape.Async)
    |> Option.orElseWith (fun () -> classifyGeneric "System.Threading.Tasks.ValueTask<" ReturnShape.ValueTask)
    |> Option.orElseWith (fun () -> classifyThreeArg "EffSharp.Core.Eff<" ReturnShape.Eff)
    |> Option.orElseWith (fun () -> classifyThreeArg "Eff<" ReturnShape.Eff)

  let private stripAbbreviation (typ: FSharpType) =
    if typ.IsAbbreviation then
      typ.AbbreviatedType
    else
      typ

  let private tryTypeDefinitionName (typ: FSharpType) =
    let normalized = stripAbbreviation typ

    if normalized.HasTypeDefinition then
      normalized.TypeDefinition.TryFullName
    else
      None

  let private resultTypeArguments renderType (typ: FSharpType) =
    let normalized = stripAbbreviation typ
    let arguments =
      if normalized.HasTypeDefinition then
        normalized.GenericArguments |> Seq.toList
      else
        []

    match tryTypeDefinitionName normalized, arguments with
    | Some "Microsoft.FSharp.Core.FSharpResult`2", [ okType; errorType ] ->
        Some(renderType okType, renderType errorType)
    | _ -> None

  let classifyReturnType renderType (typ: FSharpType) =
    let normalized = stripAbbreviation typ
    let rendered = renderType normalized
    let arguments =
      if normalized.HasTypeDefinition then
        normalized.GenericArguments |> Seq.toList
      else
        []

    match tryTypeDefinitionName normalized, arguments with
    | Some "Microsoft.FSharp.Core.FSharpResult`2", [ okType; errorType ] ->
        ReturnShape.Result(renderType okType, renderType errorType)
    | Some "System.Threading.Tasks.Task`1", [ innerType ] ->
        match resultTypeArguments renderType innerType with
        | Some(okType, errorType) -> ReturnShape.TaskResult(okType, errorType)
        | None -> ReturnShape.Task(renderType innerType)
    | Some "Microsoft.FSharp.Control.FSharpAsync`1", [ innerType ] ->
        match resultTypeArguments renderType innerType with
        | Some(okType, errorType) -> ReturnShape.AsyncResult(okType, errorType)
        | None -> ReturnShape.Async(renderType innerType)
    | Some "System.Threading.Tasks.ValueTask`1", [ innerType ] ->
        match resultTypeArguments renderType innerType with
        | Some(okType, errorType) -> ReturnShape.ValueTaskResult(okType, errorType)
        | None -> ReturnShape.ValueTask(renderType innerType)
    | Some "EffSharp.Core.Eff`3", [ okType; errorType; environmentType ] ->
        ReturnShape.Eff(renderType okType, renderType errorType, renderType environmentType)
    | Some "EffSharp.Core.Eff`2", _
    | Some "EffSharp.Core.Eff", _ ->
        ReturnShape.Unsupported(renderType normalized)
    | _ ->
        match tryClassifyFromRenderedText rendered with
        | Some shape -> shape
        | None -> ReturnShape.Plain(rendered)
