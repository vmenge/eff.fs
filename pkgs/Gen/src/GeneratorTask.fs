namespace EffSharp.Gen

open System
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

  member val ParseCommandLineArgs : ITaskItem array = [||] with get, set

  member val OtherFlags = "" with get, set

  [<Output>]
  member val GeneratedFiles : ITaskItem array = [||] with get, set

  [<Output>]
  member val OrderedCompileItems : ITaskItem array = [||] with get, set

  member private _.logDiagnostic (diagnostic: EffectDiagnostic) =
    base.Log.LogError(
      (null: string),
      diagnostic.Code,
      (null: string),
      diagnostic.FilePath,
      diagnostic.Line,
      diagnostic.Column,
      diagnostic.Line,
      diagnostic.Column,
      diagnostic.Message,
      [||]
    )

  override this.Execute() =
    try
      let compileInputs = ProjectInputs.compileInputs this.ProjectDirectory this.CompileItems
      let result =
        Generation.run {
          ProjectDirectory = this.ProjectDirectory
          IntermediateOutputPath = this.IntermediateOutputPath
          CompileInputs = compileInputs
          ParseCommandLineArgs =
            this.ParseCommandLineArgs
            |> Array.map _.ItemSpec
            |> Array.toList
          OtherFlags = this.OtherFlags
        }

      if result.Diagnostics.IsEmpty then
        this.GeneratedFiles <-
          result.GeneratedFiles
          |> Array.map (fun generatedFile -> TaskItem(generatedFile.OutputPath) :> ITaskItem)

        this.OrderedCompileItems <-
          result.OrderedCompileItems
          |> Array.map (fun filePath -> TaskItem(filePath) :> ITaskItem)

        true
      else
        for diagnostic in result.Diagnostics do
          this.logDiagnostic diagnostic

        this.GeneratedFiles <- [||]
        this.OrderedCompileItems <- [||]
        false
    with error ->
      this.Log.LogErrorFromException(error, true, true, null)
      false
