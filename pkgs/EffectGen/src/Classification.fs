namespace EffFs.EffectGen

open FSharp.Compiler.Syntax

module Classification =
  let private isNamedType expected typeName =
    match typeName with
    | SynType.LongIdent(SynLongIdent([ ident ], _, _)) -> ident.idText = expected
    | _ -> false

  let classifyReturnType renderType synType =
    match synType with
    | SynType.App(typeName, _, [ okType; errorType ], _, _, _, _)
      when isNamedType "Result" typeName ->
        ReturnShape.Result(renderType okType, renderType errorType)
    | SynType.App(typeName, _, _, _, _, _, _)
      when isNamedType "Task" typeName
           || isNamedType "Async" typeName
           || isNamedType "ValueTask" typeName
           || isNamedType "Eff" typeName ->
        ReturnShape.Unsupported(renderType synType)
    | SynType.LongIdent(SynLongIdent([ ident ], _, _))
      when ident.idText = "Task"
           || ident.idText = "Async"
           || ident.idText = "ValueTask"
           || ident.idText = "Eff" ->
        ReturnShape.Unsupported(renderType synType)
    | _ -> ReturnShape.Plain(renderType synType)
