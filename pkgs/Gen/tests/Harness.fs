namespace EffSharp.Gen.Tests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Reflection
open System.Runtime.Loader
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
    Path.Combine(repoRoot, "pkgs", "Gen", "src", "Gen.fsproj")

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

  let private restoreKey (projectPath: string) (extraArgs: string list) =
    projectPath + "\u001f" + String.concat "\u001f" extraArgs

  let private projectRestores = Dictionary<string, Task>()
  let private projectRestoresLock = obj()
  let private prerequisiteBuilds = Dictionary<string, Task>()
  let private prerequisiteBuildsLock = obj()

  let private ensureProjectRestore (projectPath: string) (extraArgs: string list) = task {
    let key = restoreKey projectPath extraArgs
    let restoreTask =
      lock projectRestoresLock (fun () ->
        match projectRestores.TryGetValue(key) with
        | true, existing -> existing
        | false, _ ->
            let started =
              task {
                let extra =
                  match extraArgs with
                  | [] -> ""
                  | args -> " " + String.concat " " args

                let! result = runDotnet None $"restore \"{projectPath}\" --nologo -m:1{extra}"

                if result.ExitCode <> 0 then
                  failwith $"failed to restore project {projectPath}{System.Environment.NewLine}{result.Output}"
              } :> Task

            projectRestores[key] <- started
            started)

    do! restoreTask
  }

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
    do! ensureProjectRestore projectPath []
    return! runDotnet None $"build \"{projectPath}\" --nologo -m:1 --no-restore"
  }

  let buildProjectWithArgs (projectPath: string) (extraArgs: string list) : Task<BuildResult> = task {
    let extra =
      match extraArgs with
      | [] -> ""
      | args -> " " + String.concat " " args

    do! ensureProjectRestore projectPath extraArgs
    return! runDotnet None $"build \"{projectPath}\" --nologo -m:1 --no-restore{extra}"
  }

  let runBuiltProject (projectPath: string) : Task<BuildResult> =
    runDotnet None $"\"{builtDllPath projectPath}\""

  type private BuiltAssemblyLoadContext(assemblyPath: string) =
    inherit AssemblyLoadContext(isCollectible = true)

    let resolver = AssemblyDependencyResolver(assemblyPath)

    override this.Load(assemblyName: AssemblyName) =
      match resolver.ResolveAssemblyToPath(assemblyName) with
      | null -> null
      | path -> this.LoadFromAssemblyPath(path)

  let runBuiltFunction (projectPath: string) (typeName: string) (methodName: string) : Task<BuildResult> = task {
    let assemblyPath = builtDllPath projectPath
    let loadContext = new BuiltAssemblyLoadContext(assemblyPath)

    let result =
      try
        let assembly = loadContext.LoadFromAssemblyPath(assemblyPath)
        let declaringType = assembly.GetType(typeName, throwOnError = true)
        let methodInfo = declaringType.GetMethod(methodName, BindingFlags.Public ||| BindingFlags.NonPublic ||| BindingFlags.Static)

        if isNull methodInfo then
          {
            ExitCode = 1
            Output = $"method {typeName}.{methodName} was not found in {assemblyPath}"
          }
        else
          let value = methodInfo.Invoke(null, [||])

          {
            ExitCode = 0
            Output =
              match value with
              | null -> ""
              | output -> string output
          }
      with error ->
        let unwrapped =
          match error with
          | :? TargetInvocationException as invocationError when not (isNull invocationError.InnerException) ->
              invocationError.InnerException
          | _ -> error

        {
          ExitCode = 1
          Output = unwrapped.ToString()
        }

    loadContext.Unload()
    return result
  }

  let packProject (projectPath: string) (outputDirectory: string) : Task<BuildResult> = task {
    do! ensureProjectBuild effectGenProject
    return! runDotnet None $"pack \"{projectPath}\" --nologo --no-build --no-restore -p:Configuration=Debug -o \"{outputDirectory}\""
  }
