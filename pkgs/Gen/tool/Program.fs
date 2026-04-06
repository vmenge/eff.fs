module EffSharp.Gen.Tool.Program

open System
open System.IO
open EffSharp.Gen

let private fail message =
  eprintfn "%s" message
  1

let private tryFindValue flag (args: string array) =
  args
  |> Array.tryFindIndex ((=) flag)
  |> Option.bind (fun index ->
    if index + 1 < args.Length then
      Some args[index + 1]
    else
      None)

let private readLines path =
  if File.Exists(path) then
    File.ReadAllLines(path)
    |> Array.toList
    |> List.filter (String.IsNullOrWhiteSpace >> not)
  else
    []

let private writeLines (path: string) (lines: string array) =
  let directory = Path.GetDirectoryName(path)

  if not (String.IsNullOrWhiteSpace(directory)) then
    Directory.CreateDirectory(directory) |> ignore

  File.WriteAllLines(path, lines)

let private formatDiagnostic (diagnostic: EffectDiagnostic) =
  $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): error {diagnostic.Code}: {diagnostic.Message}"

[<EntryPoint>]
let main argv =
  let required flag =
    match tryFindValue flag argv with
    | Some value -> Ok value
    | None -> Error $"missing required argument {flag}"

  match
    required "--project-directory",
    required "--intermediate-output-path",
    required "--compile-items-file",
    required "--parse-args-file",
    required "--ordered-compile-items-file",
    required "--other-flags-file"
  with
  | Ok projectDirectory,
    Ok intermediateOutputPath,
    Ok compileItemsFile,
    Ok parseArgsFile,
    Ok orderedCompileItemsFile,
    Ok otherFlagsFile ->
      try
        let compileInputs =
          readLines compileItemsFile
          |> List.map (fun fullPath -> {
            ItemSpec = fullPath
            FullPath = fullPath
          })
          |> List.toArray

        let result =
          Generation.run {
            ProjectDirectory = projectDirectory
            IntermediateOutputPath = intermediateOutputPath
            CompileInputs = compileInputs
            ParseCommandLineArgs = readLines parseArgsFile
            OtherFlags =
              if File.Exists(otherFlagsFile) then
                File.ReadAllText(otherFlagsFile)
              else
                ""
          }

        if result.Diagnostics.IsEmpty then
          writeLines orderedCompileItemsFile result.OrderedCompileItems
          0
        else
          for diagnostic in result.Diagnostics do
            eprintfn "%s" (formatDiagnostic diagnostic)

          1
      with error ->
        eprintfn "%s" (error.ToString())
        1
  | _ ->
      fail
        "usage: Gen.Tool --project-directory <path> --intermediate-output-path <path> --compile-items-file <path> --parse-args-file <path> --ordered-compile-items-file <path> --other-flags-file <path>"
