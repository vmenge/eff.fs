namespace EffFs.EffectGen

open System
open System.IO
open Microsoft.Build.Framework
open Microsoft.Build.Utilities

type GenerateEffectFilesTask() =
  inherit Task()

  [<Required>]
  member val ProjectDirectory = "" with get, set

  [<Required>]
  member val IntermediateOutputPath = "" with get, set

  [<Required>]
  member val CompileItems : ITaskItem array = [||] with get, set

  [<Output>]
  member val GeneratedFiles : ITaskItem array = [||] with get, set

  [<Output>]
  member val OrderedCompileItems : ITaskItem array = [||] with get, set

  member private _.generate (compileInputs: CompileInput array) (outputDirectory: string) =
    Directory.CreateDirectory(outputDirectory) |> ignore

    for staleFile in Directory.GetFiles(outputDirectory, "*.g.fs") do
      File.Delete(staleFile)

    let generatedFiles =
      compileInputs
      |> Array.collect (fun compileInput ->
        let parsedFile = FcsParsing.parseFile compileInput.FullPath

        Discovery.discoverInterfaces parsedFile
        |> List.map (fun effectInterface ->
          let outputPath = Path.Combine(outputDirectory, $"{effectInterface.EnvironmentName}.g.fs")

          {
            SourceFile = effectInterface.SourceFile
            OutputPath = outputPath
            Contents = Emission.emitFile effectInterface
          })
        |> List.toArray)

    for generatedFile in generatedFiles do
      File.WriteAllText(generatedFile.OutputPath, generatedFile.Contents)

    generatedFiles

  member private _.orderedCompileItems (compileInputs: CompileInput array) (generatedFiles: GeneratedFile array) =
    let generatedBySource =
      generatedFiles
      |> Array.groupBy _.SourceFile
      |> Map.ofArray

    let orderedItems = ResizeArray<ITaskItem>()

    for compileInput in compileInputs do
      orderedItems.Add(TaskItem(compileInput.FullPath) :> ITaskItem)

      match Map.tryFind compileInput.FullPath generatedBySource with
      | Some generatedForSource ->
          for generatedFile in generatedForSource do
            orderedItems.Add(TaskItem(generatedFile.OutputPath) :> ITaskItem)
      | None -> ()

    orderedItems.ToArray()

  override this.Execute() =
    try
      let compileInputs = ProjectInputs.compileInputs this.ProjectDirectory this.CompileItems
      let outputDirectory = ProjectInputs.generatedOutputDirectory this.ProjectDirectory this.IntermediateOutputPath
      let generatedFiles = this.generate compileInputs outputDirectory

      this.GeneratedFiles <-
        generatedFiles
        |> Array.map (fun generatedFile -> TaskItem(generatedFile.OutputPath) :> ITaskItem)

      this.OrderedCompileItems <- this.orderedCompileItems compileInputs generatedFiles
      true
    with error ->
      this.Log.LogErrorFromException(error, true, true, null)
      false
