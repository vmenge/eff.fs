namespace EffSharp.Gen

open System.Text

module Emission =
  let private appendLine (builder: StringBuilder) (line: string) = builder.AppendLine(line) |> ignore

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
    | ReturnShape.ValueTask valueType -> $"Eff<{valueType}, 'e, #{environmentName}>"
    | ReturnShape.Result(okType, errorType)
    | ReturnShape.TaskResult(okType, errorType)
    | ReturnShape.AsyncResult(okType, errorType)
    | ReturnShape.ValueTaskResult(okType, errorType)
    | ReturnShape.Eff(okType, errorType, _) -> $"Eff<{okType}, {errorType}, #{environmentName}>"
    | ReturnShape.Unsupported rawType -> failwith $"Unsupported return type reached emission: {rawType}"

  let private emitMethod builder effectInterface methodModel =
    let parameters = parameterList methodModel.ParameterGroups
    let parametersWithSpacing = if parameters = "" then "()" else parameters

    appendLine
      builder
      $"  let {methodModel.WrapperName} {parametersWithSpacing} : {returnSignature effectInterface.EnvironmentName methodModel.ReturnShape} ="

    appendLine
      builder
      $"    Eff.read (fun (env: #{effectInterface.EnvironmentName}) -> env.{effectInterface.PropertyName}.{methodModel.SourceName}{invocationSuffix methodModel})"

    match methodModel.ReturnShape with
    | ReturnShape.Plain _ -> ()
    | ReturnShape.Result _ -> appendLine builder "    |> Eff.bind Eff.ofResult"
    | ReturnShape.Task _ ->
        appendLine builder "    |> Eff.bind (fun taskValue -> Eff.ofTask (fun () -> taskValue))"
    | ReturnShape.TaskResult _ ->
        appendLine builder "    |> Eff.bind (fun taskValue -> Eff.ofTask (fun () -> taskValue))"
        appendLine builder "    |> Eff.bind Eff.ofResult"
    | ReturnShape.Async _ ->
        appendLine builder "    |> Eff.bind (fun asyncValue -> Eff.ofAsync (fun () -> asyncValue))"
    | ReturnShape.AsyncResult _ ->
        appendLine builder "    |> Eff.bind (fun asyncValue -> Eff.ofAsync (fun () -> asyncValue))"
        appendLine builder "    |> Eff.bind Eff.ofResult"
    | ReturnShape.ValueTask _ ->
        appendLine builder "    |> Eff.bind (fun valueTaskValue -> Eff.ofValueTask (fun () -> valueTaskValue))"
    | ReturnShape.ValueTaskResult _ ->
        appendLine builder "    |> Eff.bind (fun valueTaskValue -> Eff.ofValueTask (fun () -> valueTaskValue))"
        appendLine builder "    |> Eff.bind Eff.ofResult"
    | ReturnShape.Eff(_, _, environmentType) ->
        if environmentType = "unit" then
          appendLine builder "    |> Eff.map (Eff.provideFrom (fun _ -> ()))"
          appendLine builder "    |> Eff.flatten"
        elif environmentType = effectInterface.EnvironmentName || environmentType = $"#{effectInterface.EnvironmentName}" then
          appendLine builder "    |> Eff.flatten"
        elif effectInterface.InheritedEnvironments |> List.contains environmentType then
          appendLine
            builder
            $"    |> Eff.map (Eff.provideFrom (fun (outer: #{effectInterface.EnvironmentName}) -> outer :> {environmentType}))"

          appendLine builder "    |> Eff.flatten"
        else
          failwith $"Unsupported Eff environment adaptation target in W4: {environmentType}"
    | ReturnShape.Unsupported _ -> ()

    appendLine builder ""

  let emitFile effectInterface =
    let builder = StringBuilder()

    match effectInterface.Namespace with
    | Some namespaceName ->
        appendLine builder $"namespace {namespaceName}"
        appendLine builder ""
    | None -> ()

    appendLine builder "open EffSharp.Core"
    appendLine builder ""
    appendLine builder $"type {effectInterface.EnvironmentName} ="

    if effectInterface.InheritedEnvironments.IsEmpty then
      appendLine builder $"  abstract {effectInterface.PropertyName}: {effectInterface.ServiceName}"
    else
      for inheritedEnvironment in effectInterface.InheritedEnvironments do
        appendLine builder $"  inherit {inheritedEnvironment}"

    appendLine builder ""
    appendLine builder $"module {effectInterface.EnvironmentName} ="

    effectInterface.Methods |> List.iter (emitMethod builder effectInterface)

    builder.ToString().TrimEnd() + System.Environment.NewLine
