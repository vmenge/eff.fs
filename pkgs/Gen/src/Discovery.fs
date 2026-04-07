namespace EffSharp.Gen

open System
open FSharp.Compiler.Symbols
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

  let private hasAttributeName expectedShortName expectedAttributeName (name: string) =
    name = expectedShortName
    || name = expectedAttributeName
    || name.EndsWith($".{expectedShortName}", StringComparison.Ordinal)
    || name.EndsWith($".{expectedAttributeName}", StringComparison.Ordinal)

  let private effectAttributes (attributes: SynAttributeList list) =
    attributes
    |> List.collect (fun (attributeList: SynAttributeList) -> attributeList.Attributes)
    |> List.filter (fun attribute ->
      attribute.TypeName
      |> joinSynLongIdent
      |> hasAttributeName "Effect" "EffectAttribute")

  let private hasEffectAttribute (attributes: SynAttributeList list) =
    not (List.isEmpty (effectAttributes attributes))

  let rec private unwrapParens expr =
    match expr with
    | SynExpr.Paren(innerExpr, _, _, _) -> unwrapParens innerExpr
    | _ -> expr

  let private tryParseModeValue expr =
    let matchesModeCase expectedName longIdent =
      let names =
        match longIdent with
        | SynLongIdent(idents, _, _) -> idents |> List.map _.idText

      match List.rev names with
      | actualName :: "Mode" :: _
      | actualName :: _ when actualName = expectedName -> true
      | _ -> false

    match unwrapParens expr with
    | SynExpr.Const(SynConst.Unit, _) -> Some Mode.Direct
    | SynExpr.LongIdent(_, longIdent, _, _) when matchesModeCase "Direct" longIdent -> Some Mode.Direct
    | SynExpr.LongIdent(_, longIdent, _, _) when matchesModeCase "Wrap" longIdent -> Some Mode.Wrap
    | _ -> None

  let private effectMode attributes =
    effectAttributes attributes
    |> List.tryPick (fun attribute -> tryParseModeValue attribute.ArgExpr)
    |> Option.defaultValue Mode.Direct

  let private hasAbstractClassAttribute (attributes: SynAttributeList list) =
    attributes
    |> List.collect (fun (attributeList: SynAttributeList) -> attributeList.Attributes)
    |> List.exists (fun attribute ->
      attribute.TypeName
      |> joinSynLongIdent
      |> fun name ->
        name = "AbstractClass"
        || name = "AbstractClassAttribute"
        || name.EndsWith(".AbstractClass", StringComparison.Ordinal)
        || name.EndsWith(".AbstractClassAttribute", StringComparison.Ordinal))

  let private isInterfaceRepresentation attributes representation =
    match representation with
    | SynTypeDefnRepr.ObjectModel(SynTypeDefnKind.Interface, _, _) -> true
    | _ when hasAbstractClassAttribute attributes -> false
    | SynTypeDefnRepr.ObjectModel(_, members, _) ->
        members
        |> List.forall (function
          | SynMemberDefn.AbstractSlot _ -> true
          | _ -> false)
    | _ -> false

  let private renderParameterGroups (renderType: FSharpType -> string) (memberSymbol: FSharpMemberOrFunctionOrValue) =
    let renderGroup nextIndex (parameters: FSharpParameter seq) =
      let parameters = parameters |> Seq.toList

      match parameters with
      | [] -> [], nextIndex
      | [ parameter ] when renderType parameter.Type = "unit" ->
          [], nextIndex
      | [ parameter ] ->
          [ ParameterGroup.Single {
              Name = $"arg{nextIndex}"
              TypeName = renderType parameter.Type
            } ],
          nextIndex + 1
      | _ ->
          let renderedParameters, finalIndex =
            (([], nextIndex), parameters)
            ||> List.fold (fun (acc, index) parameter ->
              acc @ [ {
                Name = $"arg{index}"
                TypeName = renderType parameter.Type
              } ],
              index + 1)

          [ ParameterGroup.Tupled renderedParameters ], finalIndex

    (([], 1), memberSymbol.CurriedParameterGroups |> Seq.toList)
    ||> List.fold (fun (groups, nextIndex) group ->
      let renderedGroup, nextIndex' = renderGroup nextIndex group
      groups @ renderedGroup, nextIndex')
    |> fst

  let private discoverMethod (parsedFile: ParsedSourceFile) memberDefn =
    match memberDefn with
    | SynMemberDefn.AbstractSlot
        (
          SynValSig(_, SynIdent(ident, _), _, _, _, _, _, _, _, _, _, _),
          memberFlags,
          _,
          _
        ) ->
        if memberFlags.MemberKind <> SynMemberKind.Member then
          Error [
            createIdentDiagnostic
              "EFFGEN002"
              parsedFile.FilePath
              ident
              $"[<Effect>] interface member '{ident.idText}' is not supported. Only abstract methods are supported."
          ]
        else
          match FcsParsing.tryFindMemberSymbol parsedFile ident with
          | None ->
              Error [
                createIdentDiagnostic
                  "EFFGEN003"
                  parsedFile.FilePath
                  ident
                  $"[<Effect>] method '{ident.idText}' could not be resolved semantically."
              ]
          | Some memberSymbol ->
              let parameterGroups = renderParameterGroups FcsParsing.renderType memberSymbol
              let returnShape =
                Classification.classifyReturnType FcsParsing.renderType memberSymbol.ReturnParameter.Type

              match returnShape with
              | ReturnShape.Unsupported rawType ->
                  Error [
                    createIdentDiagnostic
                      "EFFGEN003"
                      parsedFile.FilePath
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
            parsedFile.FilePath
            memberDefn.Range
            "[<Effect>] interface members are not supported here. Only abstract methods are supported."
        ]

  let private discoverType (parsedFile: ParsedSourceFile) namespaceName typeDefn =
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
        let mode = effectMode attributes

        match representation with
        | SynTypeDefnRepr.ObjectModel(_, members, _) when isInterfaceRepresentation attributes representation ->
            if members.IsEmpty then
              {
                emptyResult with
                  Diagnostics = [
                    createIdentDiagnostic
                      "EFFGEN004"
                      parsedFile.FilePath
                      typeName
                      $"[<Effect>] interface '{serviceName}' must declare at least one abstract method."
                  ]
              }
            else
              let discoveredMethods, diagnostics =
                (([], []), members)
                ||> List.fold (fun (methodsAcc, diagnosticsAcc) memberDefn ->
                  match discoverMethod parsedFile memberDefn with
                  | Ok methodModel -> methodsAcc @ [ methodModel ], diagnosticsAcc
                  | Error memberDiagnostics -> methodsAcc, diagnosticsAcc @ memberDiagnostics)

              if diagnostics.IsEmpty then
                let inheritedEnvironments =
                  discoveredMethods
                  |> List.choose (fun methodModel ->
                    match methodModel.ReturnShape with
                    | ReturnShape.Eff(_, _, environmentType)
                      when environmentType <> "unit"
                           && environmentType <> Naming.environmentName mode serviceName
                           && environmentType <> $"#{Naming.environmentName mode serviceName}"
                           && environmentType <> serviceName
                           && environmentType <> $"#{serviceName}"
                           && environmentType <> joinLongIdent longId
                           && environmentType <> $"#{joinLongIdent longId}" ->
                        Some environmentType
                    | _ -> None)
                  |> List.distinct

                {
                  emptyResult with
                    Interfaces = [
                      {
                        Namespace = namespaceName
                        SourceFile = parsedFile.FilePath
                        Mode = mode
                        ServiceName = serviceName
                        ServiceTypeName = joinLongIdent longId
                        EnvironmentName = Naming.environmentName mode serviceName
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
                    parsedFile.FilePath
                    typeName
                    $"[<Effect>] can only be applied to interfaces. '{serviceName}' is not an interface."
                ]
            }
    | _ -> emptyResult

  let discoverInterfaces (parsedFile: ParsedSourceFile) =
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
                      let discovery = discoverType parsedFile namespaceName typeDefn

                      typeInterfacesAcc @ discovery.Interfaces,
                      typeDiagnosticsAcc @ discovery.Diagnostics)
                | _ -> innerInterfacesAcc, innerDiagnosticsAcc))
        |> fun (interfaces, diagnostics) -> {
          Interfaces = interfaces
          Diagnostics = diagnostics
        }
    | ParsedInput.SigFile _ -> emptyResult
