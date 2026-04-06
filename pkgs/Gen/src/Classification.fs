namespace EffSharp.Gen

open FSharp.Compiler.Syntax

module Classification =
  let private terminalTypeName synType =
    match synType with
    | SynType.LongIdent(SynLongIdent(idents, _, _)) -> idents |> List.tryLast |> Option.map _.idText
    | _ -> None

  let private isNamedType expected typeName =
    terminalTypeName typeName = Some expected

  let private resultTypeArguments renderType synType =
    match synType with
    | SynType.App(typeName, _, [ okType; errorType ], _, _, _, _)
      when isNamedType "Result" typeName ->
        Some(renderType okType, renderType errorType)
    | _ -> None

  let classifyReturnType renderType synType =
    match synType with
    | SynType.App(typeName, _, [ okType; errorType ], _, _, _, _)
      when isNamedType "Result" typeName ->
        ReturnShape.Result(renderType okType, renderType errorType)
    | SynType.App(typeName, _, [ innerType ], _, _, _, _)
      when isNamedType "Task" typeName ->
        match resultTypeArguments renderType innerType with
        | Some(okType, errorType) -> ReturnShape.TaskResult(okType, errorType)
        | None -> ReturnShape.Task(renderType innerType)
    | SynType.App(typeName, _, [ innerType ], _, _, _, _)
      when isNamedType "Async" typeName ->
        match resultTypeArguments renderType innerType with
        | Some(okType, errorType) -> ReturnShape.AsyncResult(okType, errorType)
        | None -> ReturnShape.Async(renderType innerType)
    | SynType.App(typeName, _, [ innerType ], _, _, _, _)
      when isNamedType "ValueTask" typeName ->
        match resultTypeArguments renderType innerType with
        | Some(okType, errorType) -> ReturnShape.ValueTaskResult(okType, errorType)
        | None -> ReturnShape.ValueTask(renderType innerType)
    | SynType.App(typeName, _, [ okType; errorType; environmentType ], _, _, _, _)
      when isNamedType "Eff" typeName ->
        ReturnShape.Eff(renderType okType, renderType errorType, renderType environmentType)
    | SynType.App(typeName, _, _, _, _, _, _)
      when isNamedType "Eff" typeName ->
        ReturnShape.Unsupported(renderType synType)
    | SynType.LongIdent _ when isNamedType "Eff" synType ->
        ReturnShape.Unsupported(renderType synType)
    | _ -> ReturnShape.Plain(renderType synType)
