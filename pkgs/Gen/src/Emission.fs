namespace EffSharp.Gen

open System
open System.Text

module Emission =
  let private appendLine (builder: StringBuilder) (line: string) = builder.AppendLine(line) |> ignore

  let private normalizeEnvironmentType (environmentType: string) =
    if environmentType.StartsWith("#", StringComparison.Ordinal) then
      environmentType.Substring(1)
    else
      environmentType

  let private isTypeVariableEnvironment (environmentType: string) =
    normalizeEnvironmentType environmentType
    |> fun normalized -> normalized.StartsWith("'", StringComparison.Ordinal)

  let private emitWrappedContract builder effectInterface =
    let effectNamespace =
      match effectInterface.Namespace with
      | Some namespaceName -> $"{namespaceName}.Effect"
      | None -> "Effect"

    appendLine builder $"namespace {effectNamespace}"
    appendLine builder ""
    appendLine builder $"type {effectInterface.PropertyName} ="

    for inheritedEnvironment in effectInterface.InheritedEnvironments do
      appendLine builder $"  inherit {inheritedEnvironment}"

    if effectInterface.InheritedEnvironments.IsEmpty then
      appendLine builder $"  abstract {effectInterface.PropertyName}: {effectInterface.ServiceTypeName}"

    appendLine builder ""

  let private parameterList parameterGroups =
    parameterGroups
    |> List.collect (function
      | Single parameter -> [ $"({parameter.Name}: {parameter.TypeName})" ]
      | Tupled parameters ->
          let tupleParameters =
            parameters
            |> List.map (fun parameter -> $"{parameter.Name}: {parameter.TypeName}")
            |> String.concat ", "

          [ $"({tupleParameters})" ])
    |> String.concat " "

  let private invocationSuffix methodModel =
    let segments =
      methodModel.ParameterGroups
      |> List.map (function
        | Single parameter -> $" {parameter.Name}"
        | Tupled parameters ->
            parameters
            |> List.map _.Name
            |> String.concat ", "
            |> fun names -> $"({names})")

    if List.isEmpty methodModel.ParameterGroups then
      "()"
    else
      String.concat "" segments

  let private returnSignature environmentName returnShape =
    match returnShape with
    | ReturnShape.Plain valueType
    | ReturnShape.Task valueType
    | ReturnShape.Async valueType
    | ReturnShape.ValueTask valueType -> $"EffSharp.Core.Eff<{valueType}, 'e, #{environmentName}>"
    | ReturnShape.Result(okType, errorType)
    | ReturnShape.TaskResult(okType, errorType)
    | ReturnShape.AsyncResult(okType, errorType)
    | ReturnShape.ValueTaskResult(okType, errorType)
    | ReturnShape.Eff(okType, errorType, _) -> $"EffSharp.Core.Eff<{okType}, {errorType}, #{environmentName}>"
    | ReturnShape.Unsupported rawType -> failwith $"Unsupported return type reached emission: {rawType}"

  let private emitMethod builder effectInterface methodModel =
    let parameters = parameterList methodModel.ParameterGroups
    let parametersWithSpacing = if parameters = "" then "()" else parameters

    appendLine
      builder
      $"    static member {methodModel.WrapperName} {parametersWithSpacing} : {returnSignature effectInterface.EnvironmentName methodModel.ReturnShape} ="

    if effectInterface.Mode = Mode.Wrap then
      appendLine
        builder
          $"      Eff.read (fun (env: #{effectInterface.EnvironmentName}) -> env.{effectInterface.PropertyName}.{methodModel.SourceName}{invocationSuffix methodModel})"
    else
      appendLine
        builder
        $"      Eff.read (fun (env: #{effectInterface.EnvironmentName}) -> env.{methodModel.SourceName}{invocationSuffix methodModel})"

    match methodModel.ReturnShape with
    | ReturnShape.Plain _ -> ()
    | ReturnShape.Result _ -> appendLine builder "      |> Eff.bind Eff.ofResult"
    | ReturnShape.Task _ ->
        appendLine builder "      |> Eff.bind (fun taskValue -> Eff.ofTask (fun () -> taskValue))"
    | ReturnShape.TaskResult _ ->
        appendLine builder "      |> Eff.bind (fun taskValue -> Eff.ofTask (fun () -> taskValue))"
        appendLine builder "      |> Eff.bind Eff.ofResult"
    | ReturnShape.Async _ ->
        appendLine builder "      |> Eff.bind (fun asyncValue -> Eff.ofAsync (fun () -> asyncValue))"
    | ReturnShape.AsyncResult _ ->
        appendLine builder "      |> Eff.bind (fun asyncValue -> Eff.ofAsync (fun () -> asyncValue))"
        appendLine builder "      |> Eff.bind Eff.ofResult"
    | ReturnShape.ValueTask _ ->
        appendLine builder "      |> Eff.bind (fun valueTaskValue -> Eff.ofValueTask (fun () -> valueTaskValue))"
    | ReturnShape.ValueTaskResult _ ->
        appendLine builder "      |> Eff.bind (fun valueTaskValue -> Eff.ofValueTask (fun () -> valueTaskValue))"
        appendLine builder "      |> Eff.bind Eff.ofResult"
    | ReturnShape.Eff(_, _, environmentType) ->
        let normalizedEnvironment = normalizeEnvironmentType environmentType

        if normalizedEnvironment = "unit" then
          appendLine builder "      |> Eff.map (Eff.provideFrom (fun _ -> ()))"
          appendLine builder "      |> Eff.flatten"
        elif isTypeVariableEnvironment environmentType
             || normalizedEnvironment = effectInterface.EnvironmentName
             || normalizedEnvironment = effectInterface.ServiceName
             || normalizedEnvironment = effectInterface.ServiceTypeName then
          appendLine builder "      |> Eff.flatten"
        elif effectInterface.InheritedEnvironments |> List.contains normalizedEnvironment then
          appendLine
            builder
            $"      |> Eff.map (Eff.provideFrom (fun (outer: #{effectInterface.EnvironmentName}) -> outer :> {normalizedEnvironment}))"

          appendLine builder "      |> Eff.flatten"
        else
          failwith $"Unsupported Eff environment adaptation target in W4: {environmentType}"
    | ReturnShape.Unsupported _ -> ()

    appendLine builder ""

  let emitFile effectInterface =
    let builder = StringBuilder()

    if effectInterface.Mode = Mode.Wrap then
      emitWrappedContract builder effectInterface

    match effectInterface.Namespace with
    | Some namespaceName ->
        appendLine builder $"namespace {namespaceName}"
        appendLine builder ""
    | None -> ()

    appendLine builder "open EffSharp.Core"

    appendLine builder ""
    appendLine builder "[<AutoOpen>]"
    appendLine builder $"module Generated_{effectInterface.ServiceName} ="
    appendLine builder $"  type {effectInterface.ServiceName} with"

    effectInterface.Methods |> List.iter (emitMethod builder effectInterface)

    builder.ToString().TrimEnd() + System.Environment.NewLine
