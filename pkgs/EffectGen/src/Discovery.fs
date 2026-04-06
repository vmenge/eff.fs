namespace EffFs.EffectGen

open System
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

module Discovery =
  let private emptyResult = {
    Interfaces = []
    Diagnostics = []
  }

  let private createDiagnostic code filePath line column message = {
    Code = code
    Message = message
    FilePath = filePath
    Line = line
    Column = column
  }

  let private createRangeDiagnostic code filePath (range: range) message =
    createDiagnostic code filePath range.StartLine (range.StartColumn + 1) message

  let private createIdentDiagnostic code filePath (ident: Ident) message =
    createRangeDiagnostic code filePath ident.idRange message

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

  let private isInterfaceRepresentation representation =
    match representation with
    | SynTypeDefnRepr.ObjectModel(SynTypeDefnKind.Interface, _, _) -> true
    | SynTypeDefnRepr.ObjectModel(_, members, _) ->
        members
        |> List.forall (function
          | SynMemberDefn.AbstractSlot _ -> true
          | _ -> false)
    | _ -> false

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

  let private discoverMethod filePath renderType memberDefn =
    match memberDefn with
    | SynMemberDefn.AbstractSlot
        (
          SynValSig(_, SynIdent(ident, _), _, memberType, _, _, _, _, _, _, _, _),
          memberFlags,
          _,
          _
        ) ->
        if memberFlags.MemberKind <> SynMemberKind.Member then
          Error [
            createIdentDiagnostic
              "EFFGEN002"
              filePath
              ident
              $"[<Effect>] interface member '{ident.idText}' is not supported. Only abstract methods are supported."
          ]
        else
          let parameterGroups, returnType = renderParameterGroups renderType memberType
          let returnShape = Classification.classifyReturnType renderType returnType

          match returnShape with
          | ReturnShape.Unsupported rawType ->
              Error [
                createIdentDiagnostic
                  "EFFGEN003"
                  filePath
                  ident
                  $"[<Effect>] method '{ident.idText}' has unsupported return shape '{rawType}'."
              ]
          | _ ->
              Ok {
                SourceName = ident.idText
                WrapperName = Naming.wrapperName ident.idText
                DeclarationLine = ident.idRange.StartLine
                DeclarationColumn = ident.idRange.StartColumn + 1
                ParameterGroups = parameterGroups
                ReturnShape = returnShape
              }
    | _ ->
        Error [
          createRangeDiagnostic
            "EFFGEN002"
            filePath
            memberDefn.Range
            "[<Effect>] interface members are not supported here. Only abstract methods are supported."
        ]

  let private discoverType filePath namespaceName renderType typeDefn =
    match typeDefn with
    | SynTypeDefn
        (
          SynComponentInfo(attributes, _, _, longId, _, _, _, _),
          representation,
          _,
          _,
          _,
          _
        ) when hasEffectAttribute attributes ->
        let serviceName = longId |> List.last |> _.idText
        let typeName = longId |> List.last

        match representation with
        | SynTypeDefnRepr.ObjectModel(_, members, _) when isInterfaceRepresentation representation ->
            if members.IsEmpty then
              {
                emptyResult with
                  Diagnostics = [
                    createIdentDiagnostic
                      "EFFGEN004"
                      filePath
                      typeName
                      $"[<Effect>] interface '{serviceName}' must declare at least one abstract method."
                  ]
              }
            else
              let discoveredMethods, diagnostics =
                (([], []), members)
                ||> List.fold (fun (methodsAcc, diagnosticsAcc) memberDefn ->
                  match discoverMethod filePath renderType memberDefn with
                  | Ok methodModel -> methodsAcc @ [ methodModel ], diagnosticsAcc
                  | Error memberDiagnostics -> methodsAcc, diagnosticsAcc @ memberDiagnostics)

              if diagnostics.IsEmpty then
                let inheritedEnvironments =
                  discoveredMethods
                  |> List.choose (fun methodModel ->
                    match methodModel.ReturnShape with
                    | ReturnShape.Eff(_, _, environmentType)
                      when environmentType <> "unit"
                           && environmentType <> Naming.environmentName serviceName
                           && environmentType <> $"#{Naming.environmentName serviceName}" ->
                        Some environmentType
                    | _ -> None)
                  |> List.distinct

                {
                  emptyResult with
                    Interfaces = [
                      {
                        Namespace = namespaceName
                        SourceFile = filePath
                        ServiceName = serviceName
                        EnvironmentName = Naming.environmentName serviceName
                        PropertyName = Naming.propertyName serviceName
                        DeclarationLine = typeName.idRange.StartLine
                        DeclarationColumn = typeName.idRange.StartColumn + 1
                        InheritedEnvironments = inheritedEnvironments
                        Methods = discoveredMethods
                      }
                    ]
                }
              else
                { emptyResult with Diagnostics = diagnostics }
        | _ ->
            {
              emptyResult with
                Diagnostics = [
                  createIdentDiagnostic
                    "EFFGEN001"
                    filePath
                    typeName
                    $"[<Effect>] can only be applied to interfaces. '{serviceName}' is not an interface."
                ]
            }
    | _ -> emptyResult

  let discoverInterfaces (parsedFile: ParsedSourceFile) =
    let textForRange = textInRange parsedFile.Source
    let renderType (synType: SynType) = textForRange synType.Range

    match parsedFile.ParseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(_, _, _, _, modules, _, _, _)) ->
        (([], []), modules)
        ||> List.fold (fun (interfacesAcc, diagnosticsAcc) moduleOrNamespace ->
          match moduleOrNamespace with
          | SynModuleOrNamespace(longId, _, _, declarations, _, _, _, _, _) ->
              let namespaceName =
                if longId.IsEmpty then
                  None
                else
                  Some (joinLongIdent longId)

              ((interfacesAcc, diagnosticsAcc), declarations)
              ||> List.fold (fun (innerInterfacesAcc, innerDiagnosticsAcc) declaration ->
                match declaration with
                | SynModuleDecl.Types(typeDefns, _) ->
                    ((innerInterfacesAcc, innerDiagnosticsAcc), typeDefns)
                    ||> List.fold (fun (typeInterfacesAcc, typeDiagnosticsAcc) typeDefn ->
                      let discovery = discoverType parsedFile.FilePath namespaceName renderType typeDefn

                      typeInterfacesAcc @ discovery.Interfaces,
                      typeDiagnosticsAcc @ discovery.Diagnostics)
                | _ -> innerInterfacesAcc, innerDiagnosticsAcc))
        |> fun (interfaces, diagnostics) -> {
          Interfaces = interfaces
          Diagnostics = diagnostics
        }
    | ParsedInput.SigFile _ -> emptyResult
