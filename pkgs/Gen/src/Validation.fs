namespace EffSharp.Gen

open System
open FSharp.Compiler.Symbols
open FSharp.Compiler.Syntax

module Validation =
  type private EnvironmentContract = {
    Names: string list
    Properties: (string * string) list
  }

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
    if environmentType.StartsWith("#", StringComparison.Ordinal) then
      environmentType.Substring(1)
    else
      environmentType

  let private isTypeVariableEnvironment (environmentType: string) =
    normalizeEnvironmentType environmentType
    |> fun normalized -> normalized.StartsWith("'", StringComparison.Ordinal)

  let private matchesTypeName (candidate: string) (typeName: string) =
    typeName = candidate || typeName.EndsWith($".{candidate}", StringComparison.Ordinal)

  let private equivalentTypeName left right =
    matchesTypeName left right || matchesTypeName right left

  let private matchesOwnEnvironment effectInterface environmentType =
    let normalizedEnvironment = normalizeEnvironmentType environmentType

    isTypeVariableEnvironment environmentType
    || equivalentTypeName effectInterface.EnvironmentName normalizedEnvironment
    || equivalentTypeName effectInterface.ServiceName normalizedEnvironment
    || equivalentTypeName effectInterface.ServiceTypeName normalizedEnvironment

  let private discoverEnvironmentContracts (parsedFile: ParsedSourceFile) =
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
                  Some(joinLongIdent longId)

              declarations
              |> List.collect (fun declaration ->
                match declaration with
                | SynModuleDecl.Types(typeDefns, _) ->
                    typeDefns
                    |> List.choose (fun typeDefn ->
                      match typeDefn with
                      | SynTypeDefn(SynComponentInfo(_, _, _, typeName, _, _, _, _), SynTypeDefnRepr.ObjectModel(_, members, _), _, _, _, _) ->
                          let serviceName = typeName |> List.last |> _.idText

                          let properties =
                            members
                            |> List.choose (fun memberDefn ->
                              match memberDefn with
                              | SynMemberDefn.AbstractSlot(SynValSig(_, SynIdent(ident, _), _, _, _, _, _, _, _, _, _, _), memberFlags, _, _)
                                when memberFlags.MemberKind = SynMemberKind.PropertyGet ->
                                  match FcsParsing.tryFindMemberSymbol parsedFile ident with
                                  | Some propertySymbol ->
                                      Some(ident.idText, FcsParsing.renderType propertySymbol.ReturnParameter.Type)
                                  | None -> None
                              | _ -> None)

                          if properties.IsEmpty then
                            None
                          else
                            let names =
                              serviceName
                              :: [
                                   match namespaceName with
                                   | Some ns -> $"{ns}.{serviceName}"
                                   | None -> ()
                                 ]

                            Some {
                              Names = names
                              Properties = properties
                            }
                      | _ -> None)
                | _ -> []))
    | ParsedInput.SigFile _ -> []

  let private adaptInterface environmentContracts effectInterface =
    let inheritedEnvironments, diagnostics =
      (([], []), effectInterface.Methods)
      ||> List.fold (fun (inheritedAcc, diagnosticsAcc) methodModel ->
        match methodModel.ReturnShape with
        | ReturnShape.Eff(_, _, environmentType) when environmentType <> "unit" && not (matchesOwnEnvironment effectInterface environmentType) ->
            let normalizedEnvironment = normalizeEnvironmentType environmentType

            let isMechanicallyDerivable =
              if effectInterface.Mode = Mode.Wrap then
                  environmentContracts
                  |> List.tryFind (fun contract ->
                    contract.Names |> List.exists (fun name -> matchesTypeName name normalizedEnvironment))
                  |> Option.exists (fun contract ->
                    match contract.Properties with
                    | [ propertyName, propertyType ] ->
                        propertyName = effectInterface.PropertyName
                        && equivalentTypeName effectInterface.ServiceTypeName propertyType
                    | _ -> false)
              else
                false

            if isMechanicallyDerivable then
              inheritedAcc @ [ normalizedEnvironment ], diagnosticsAcc
            else
              inheritedAcc,
              diagnosticsAcc
              @ [
                  createDiagnostic
                    "EFFGEN003"
                    effectInterface.SourceFile
                    methodModel.DeclarationLine
                    methodModel.DeclarationColumn
                    $"[<Effect>] method '{methodModel.SourceName}' has unsupported return shape '{environmentType}' because environment adaptation to '{normalizedEnvironment}' is not mechanically derivable."
                ]
        | _ -> inheritedAcc, diagnosticsAcc)

    if diagnostics.IsEmpty then
      Ok {
        effectInterface with
            InheritedEnvironments = inheritedEnvironments |> List.distinct
      }
    else
      Error diagnostics

  let private collisionDiagnostics effectInterfaces =
    effectInterfaces
    |> List.filter (fun effectInterface -> effectInterface.Mode = Mode.Wrap)
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

  let validateFiles (parsedFiles: ParsedSourceFile array) =
    let discovered =
      parsedFiles |> Array.map Discovery.discoverInterfaces

    let environmentContracts =
      parsedFiles
      |> Array.collect (discoverEnvironmentContracts >> List.toArray)
      |> Array.toList

    let discoveredInterfaces =
      discovered
      |> Array.collect (_.Interfaces >> List.toArray)
      |> Array.toList

    let discoveryDiagnostics =
      discovered
      |> Array.collect (_.Diagnostics >> List.toArray)
      |> Array.toList

    let validatedInterfaces, adaptationDiagnostics =
      (([], []), discoveredInterfaces)
      ||> List.fold (fun (interfacesAcc, diagnosticsAcc) effectInterface ->
        match adaptInterface environmentContracts effectInterface with
        | Ok validatedInterface -> interfacesAcc @ [ validatedInterface ], diagnosticsAcc
        | Error diagnostics -> interfacesAcc, diagnosticsAcc @ diagnostics)

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
