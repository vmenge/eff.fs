namespace EffSharp.Gen

open System
open System.IO
open Microsoft.Build.Framework

type CompileInput = {
  ItemSpec: string
  FullPath: string
}

module ProjectInputs =
  let private normalizePath (projectDirectory: string) (path: string) =
    if Path.IsPathRooted(path) then
      Path.GetFullPath(path)
    else
      Path.GetFullPath(Path.Combine(projectDirectory, path))

  let compileInputs (projectDirectory: string) (compileItems: ITaskItem array) =
    compileItems
    |> Array.map (fun item ->
      let itemSpec = item.ItemSpec
      let fullPath =
        let metadataPath = item.GetMetadata("FullPath")

        if String.IsNullOrWhiteSpace(metadataPath) then
          normalizePath projectDirectory itemSpec
        else
          normalizePath projectDirectory metadataPath

      {
        ItemSpec = itemSpec
        FullPath = fullPath
      })

  let generatedOutputDirectory (projectDirectory: string) (intermediateOutputPath: string) =
    normalizePath projectDirectory intermediateOutputPath
    |> fun path -> Path.Combine(path, "Gen")
