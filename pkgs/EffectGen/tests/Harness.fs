namespace EffFs.EffectGen.Tests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading.Tasks

module Harness =
  type BuildResult = {
    ExitCode: int
    Output: string
  }

  let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", ".."))

  let private coreProject =
    Path.Combine(repoRoot, "pkgs", "Core", "src", "Core.fsproj")

  let private effectGenProject =
    Path.Combine(repoRoot, "pkgs", "EffectGen", "src", "EffectGen.fsproj")

  let runProcess (workingDirectory: string option) (fileName: string) (arguments: string) : Task<BuildResult> = task {
    let startInfo = ProcessStartInfo(fileName, arguments)
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false

    match workingDirectory with
    | Some directory -> startInfo.WorkingDirectory <- directory
    | None -> ()

    use proc = new Process(StartInfo = startInfo)

    if not (proc.Start()) then
      failwith $"failed to start {fileName} {arguments}"

    let stdoutTask = proc.StandardOutput.ReadToEndAsync()
    let stderrTask = proc.StandardError.ReadToEndAsync()
    do! proc.WaitForExitAsync()
    let! stdout = stdoutTask
    let! stderr = stderrTask

    return {
      ExitCode = proc.ExitCode
      Output = stdout + stderr
    }
  }

  let private runDotnet (workingDirectory: string option) (arguments: string) =
    runProcess workingDirectory "dotnet" arguments

  let private builtDllPath (projectPath: string) =
    let projectDirectory = Path.GetDirectoryName(projectPath)
    let projectName = Path.GetFileNameWithoutExtension(projectPath)
    Path.Combine(projectDirectory, "bin", "Debug", "net10.0", $"{projectName}.dll")

  let private prerequisiteBuilds = Dictionary<string, Task>()
  let private prerequisiteBuildsLock = obj()

  let private ensureProjectBuild (projectPath: string) = task {
    let buildTask =
      lock prerequisiteBuildsLock (fun () ->
        match prerequisiteBuilds.TryGetValue(projectPath) with
        | true, existing -> existing
        | false, _ ->
            let started =
              task {
                let! result = runDotnet None $"build \"{projectPath}\" --nologo -m:1"

                if result.ExitCode <> 0 then
                  failwith $"failed to build prerequisite project {projectPath}{System.Environment.NewLine}{result.Output}"
              } :> Task

            prerequisiteBuilds[projectPath] <- started
            started)

    do! buildTask
  }

  let buildProject (projectPath: string) : Task<BuildResult> = task {
    do! ensureProjectBuild coreProject
    do! ensureProjectBuild effectGenProject
    return! runDotnet None $"build \"{projectPath}\" --nologo -m:1 -t:Rebuild"
  }

  let buildProjectWithArgs (projectPath: string) (extraArgs: string list) : Task<BuildResult> = task {
    do! ensureProjectBuild coreProject
    do! ensureProjectBuild effectGenProject

    let extra =
      match extraArgs with
      | [] -> ""
      | args -> " " + String.concat " " args

    return! runDotnet None $"build \"{projectPath}\" --nologo -m:1 -t:Rebuild{extra}"
  }

  let runBuiltProject (projectPath: string) : Task<BuildResult> =
    runDotnet None $"\"{builtDllPath projectPath}\""

  let runBuiltExpression (projectPath: string) (expression: string) : Task<BuildResult> = task {
    let scriptPath = Path.Combine(Path.GetTempPath(), $"effectgen-run-{Guid.NewGuid():N}.fsx")

    try
      File.WriteAllText(
        scriptPath,
        "#r @\"" + builtDllPath projectPath + "\"" + System.Environment.NewLine
        + "printfn \"%s\" (" + expression + ")" + System.Environment.NewLine
      )

      return! runDotnet None $"fsi --nologo --exec \"{scriptPath}\""
    finally
      if File.Exists(scriptPath) then
        File.Delete(scriptPath)
  }

  let packProject (projectPath: string) (outputDirectory: string) : Task<BuildResult> = task {
    do! ensureProjectBuild coreProject
    do! ensureProjectBuild effectGenProject
    return! runDotnet None $"pack \"{projectPath}\" --nologo --no-build -p:Configuration=Debug -o \"{outputDirectory}\""
  }
