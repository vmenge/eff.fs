namespace EffFs.EffectGen

open System
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

module Discovery =
  let private joinLongIdent (idents: Ident list) =
    idents |> List.map _.idText |> String.concat "."

  let private joinSynLongIdent (SynLongIdent(idents, _, _)) = joinLongIdent idents

  let private buildLineOffsets (source: string) =
    let offsets = ResizeArray<int>()
    offsets.Add(0)

    for index in 0 .. source.Length - 1 do
      if source[index] = '\n' then
        offsets.Add(index + 1)

    offsets.ToArray()

  let private textInRange (source: string) =
    let lineOffsets = buildLineOffsets source

    fun (range: range) ->
      let startOffset = lineOffsets[range.StartLine - 1] + range.StartColumn
      let endOffset = lineOffsets[range.EndLine - 1] + range.EndColumn
      source.Substring(startOffset, endOffset - startOffset).Trim()

  let private hasEffectAttribute (attributes: SynAttributeList list) =
    let isEffectName (name: string) =
      name = "Effect" || name = "EffectAttribute" || name.EndsWith(".Effect") || name.EndsWith(".EffectAttribute")

    attributes
    |> List.collect (fun (attributeList: SynAttributeList) -> attributeList.Attributes)
    |> List.exists (fun attribute ->
      attribute.TypeName
      |> joinSynLongIdent
      |> isEffectName)

  let private renderParameterGroups renderType synType =
    let rec loop nextIndex groups currentType =
      match currentType with
      | SynType.Fun(argumentType, returnType, _, _) ->
          let group, nextIndex' = renderArgumentGroup nextIndex argumentType
          loop nextIndex' (groups @ group) returnType
      | _ -> groups, currentType

    and renderArgumentGroup nextIndex argumentType =
      match argumentType with
      | SynType.LongIdent(SynLongIdent([ ident ], _, _)) when ident.idText = "unit" ->
          [], nextIndex
      | SynType.Tuple(false, segments, _) ->
          let parameters, finalIndex =
            (([], nextIndex), segments)
            ||> List.fold (fun (acc, index) segment ->
              match segment with
              | SynTupleTypeSegment.Type tupleType ->
                  let parameter = {
                    Name = $"arg{index}"
                    TypeName = renderType tupleType
                  }

                  acc @ [ parameter ], index + 1
              | SynTupleTypeSegment.Star _
              | SynTupleTypeSegment.Slash _ -> acc, index)

          [ ParameterGroup.Tupled parameters ], finalIndex
      | _ ->
          let parameter = {
            Name = $"arg{nextIndex}"
            TypeName = renderType argumentType
          }

          [ ParameterGroup.Single parameter ], nextIndex + 1

    loop 1 [] synType

  let private discoverMethod renderType memberDefn =
    match memberDefn with
    | SynMemberDefn.AbstractSlot
        (
          SynValSig(_, SynIdent(ident, _), _, memberType, _, _, _, _, _, _, _, _),
          _,
          _,
          _
        ) ->
        let parameterGroups, returnType = renderParameterGroups renderType memberType

        Some {
          SourceName = ident.idText
          WrapperName = Naming.wrapperName ident.idText
          ParameterGroups = parameterGroups
          ReturnShape = Classification.classifyReturnType renderType returnType
        }
    | _ -> None

  let private discoverType namespaceName renderType typeDefn =
    match typeDefn with
    | SynTypeDefn
        (
          SynComponentInfo(attributes, _, _, longId, _, _, _, _),
          SynTypeDefnRepr.ObjectModel(_, members, _),
          _,
          _,
          _,
          _
        ) when hasEffectAttribute attributes ->
        let serviceName = longId |> List.last |> _.idText
        let methods = members |> List.choose (discoverMethod renderType)

        match methods with
        | [] -> None
        | discoveredMethods ->
            let hasUnsupported =
              discoveredMethods
              |> List.exists (fun methodModel ->
                match methodModel.ReturnShape with
                | ReturnShape.Unsupported _ -> true
                | _ -> false)

            if hasUnsupported then
              None
            else
              Some {
                Namespace = namespaceName
                SourceFile = ""
                ServiceName = serviceName
                EnvironmentName = Naming.environmentName serviceName
                PropertyName = Naming.propertyName serviceName
                Methods = discoveredMethods
              }
    | _ -> None

  let discoverInterfaces (parsedFile: ParsedSourceFile) =
    let textForRange = textInRange parsedFile.Source
    let renderType (synType: SynType) = textForRange synType.Range

    match parsedFile.ParseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(_, _, _, _, modules, _, _, _)) ->
        modules
        |> List.collect (fun moduleOrNamespace ->
          match moduleOrNamespace with
          | SynModuleOrNamespace(longId, _, _, declarations, _, _, _, _, _) ->
              let namespaceName =
                if longId.IsEmpty then
                  None
                else
                  Some (joinLongIdent longId)

              declarations
              |> List.collect (fun declaration ->
                match declaration with
                | SynModuleDecl.Types(typeDefns, _) ->
                    typeDefns
                    |> List.choose (discoverType namespaceName renderType)
                    |> List.map (fun effectInterface -> { effectInterface with SourceFile = parsedFile.FilePath })
                | _ -> []))
    | ParsedInput.SigFile _ -> []
