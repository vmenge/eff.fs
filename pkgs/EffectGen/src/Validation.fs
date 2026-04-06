namespace EffFs.EffectGen

open FSharp.Compiler.Syntax

module Validation =
  type private DefinedTypeKind =
    | Interface
    | NonInterface

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

  let private joinLongIdent (idents: Ident list) =
    idents |> List.map _.idText |> String.concat "."

  let private normalizeEnvironmentType (environmentType: string) =
    if environmentType.StartsWith("#") then
      environmentType.Substring(1)
    else
      environmentType

  let private isInterfaceRepresentation representation =
    match representation with
    | SynTypeDefnRepr.ObjectModel(SynTypeDefnKind.Interface, _, _) -> true
    | SynTypeDefnRepr.ObjectModel(_, members, _) ->
        members
        |> List.forall (function
          | SynMemberDefn.AbstractSlot _ -> true
          | _ -> false)
    | _ -> false

  let private discoverDefinedTypes (parsedFile: ParsedSourceFile) =
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
                    |> List.collect (fun (SynTypeDefn(SynComponentInfo(_, _, _, typeName, _, _, _, _), representation, _, _, _, _)) ->
                      let serviceName = typeName |> List.last |> _.idText
                      let kind =
                        if isInterfaceRepresentation representation then Interface else NonInterface

                      let fullyQualifiedName =
                        namespaceName
                        |> Option.map (fun ns -> $"{ns}.{serviceName}")

                      [
                        yield serviceName, kind

                        match fullyQualifiedName with
                        | Some fullName -> yield fullName, kind
                        | None -> ()
                      ])
                | _ -> []))
    | ParsedInput.SigFile _ -> []

  let private adaptationDiagnostics knownTypes effectInterface =
    effectInterface.Methods
    |> List.choose (fun methodModel ->
      match methodModel.ReturnShape with
      | ReturnShape.Eff(_, _, environmentType)
        when environmentType <> "unit"
             && environmentType <> effectInterface.EnvironmentName
             && environmentType <> $"#{effectInterface.EnvironmentName}" ->
          let normalizedEnvironment = normalizeEnvironmentType environmentType

          match Map.tryFind normalizedEnvironment knownTypes with
          | Some NonInterface ->
              Some {
                Code = "EFFGEN003"
                Message =
                  $"[<Effect>] method '{methodModel.SourceName}' has unsupported return shape '{environmentType}' because environment adaptation to '{normalizedEnvironment}' is not mechanically derivable."
                FilePath = effectInterface.SourceFile
                Line = methodModel.DeclarationLine
                Column = methodModel.DeclarationColumn
              }
          | _ -> None
      | _ -> None)

  let private collisionDiagnostics effectInterfaces =
    effectInterfaces
    |> List.groupBy (fun effectInterface -> effectInterface.Namespace, effectInterface.EnvironmentName, effectInterface.PropertyName)
    |> List.collect (fun ((_, environmentName, propertyName), groupedInterfaces) ->
      if groupedInterfaces.Length < 2 then
        []
      else
        let conflictingNames =
          groupedInterfaces
          |> List.map _.ServiceName
          |> String.concat ", "

        groupedInterfaces
        |> List.map (fun effectInterface ->
          createDiagnostic
            "EFFGEN005"
            effectInterface.SourceFile
            effectInterface.DeclarationLine
            effectInterface.DeclarationColumn
            $"[<Effect>] interface '{effectInterface.ServiceName}' collides with generated names from {conflictingNames}. Generated environment '{environmentName}' and property '{propertyName}' must be unique within a namespace."))

  let validateFiles (parsedFiles: ParsedSourceFile list) =
    let discovered =
      parsedFiles |> List.map Discovery.discoverInterfaces

    let knownTypes =
      parsedFiles
      |> List.collect discoverDefinedTypes
      |> Map.ofList

    let discoveredInterfaces =
      discovered
      |> List.collect _.Interfaces

    let discoveryDiagnostics =
      discovered
      |> List.collect _.Diagnostics

    let validatedInterfaces, adaptationDiagnostics =
      (([], []), discoveredInterfaces)
      ||> List.fold (fun (interfacesAcc, diagnosticsAcc) effectInterface ->
        let diagnostics = adaptationDiagnostics knownTypes effectInterface

        if diagnostics.IsEmpty then
          interfacesAcc @ [ effectInterface ], diagnosticsAcc
        else
          interfacesAcc, diagnosticsAcc @ diagnostics)

    let collisions = collisionDiagnostics validatedInterfaces

    let collisionKeys =
      collisions
      |> List.map (fun diagnostic -> diagnostic.FilePath, diagnostic.Line, diagnostic.Column)
      |> Set.ofList

    let finalInterfaces =
      validatedInterfaces
      |> List.filter (fun effectInterface ->
        not (collisionKeys.Contains(effectInterface.SourceFile, effectInterface.DeclarationLine, effectInterface.DeclarationColumn)))

    {
      Interfaces = finalInterfaces
      Diagnostics = discoveryDiagnostics @ adaptationDiagnostics @ collisions
    }
