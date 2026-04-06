namespace EffFs.EffectGen

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
    | ReturnShape.Plain valueType -> $"Eff<{valueType}, 'e, #{environmentName}>"
    | ReturnShape.Result(okType, errorType) -> $"Eff<{okType}, {errorType}, #{environmentName}>"
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
    | ReturnShape.Unsupported _ -> ()

    appendLine builder ""

  let emitFile effectInterface =
    let builder = StringBuilder()

    match effectInterface.Namespace with
    | Some namespaceName ->
        appendLine builder $"namespace {namespaceName}"
        appendLine builder ""
    | None -> ()

    appendLine builder "open EffFs.Core"
    appendLine builder ""
    appendLine builder $"type {effectInterface.EnvironmentName} ="
    appendLine builder $"  abstract {effectInterface.PropertyName}: {effectInterface.ServiceName}"
    appendLine builder ""
    appendLine builder $"module {effectInterface.EnvironmentName} ="

    effectInterface.Methods |> List.iter (emitMethod builder effectInterface)

    builder.ToString().TrimEnd() + System.Environment.NewLine
