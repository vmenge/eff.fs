namespace EffFs.EffectGen.Tests

open System.Diagnostics
open System.Threading.Tasks

module Harness =
  type BuildResult = {
    ExitCode: int
    Output: string
  }

  let private runDotnet (workingDirectory: string option) (arguments: string) : Task<BuildResult> = task {
    let startInfo = ProcessStartInfo("dotnet", arguments)
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false

    match workingDirectory with
    | Some directory -> startInfo.WorkingDirectory <- directory
    | None -> ()

    use proc = new Process(StartInfo = startInfo)

    if not (proc.Start()) then
      failwith $"failed to start dotnet {arguments}"

    do! proc.WaitForExitAsync()

    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()

    return {
      ExitCode = proc.ExitCode
      Output = stdout + stderr
    }
  }

  let buildProject (projectPath: string) : Task<BuildResult> =
    runDotnet None $"build \"{projectPath}\" --nologo -t:Rebuild"

  let packProject (projectPath: string) (outputDirectory: string) : Task<BuildResult> =
    runDotnet None $"pack \"{projectPath}\" --nologo -o \"{outputDirectory}\""
