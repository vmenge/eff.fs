namespace EffSharp.Gen.Tests

open System
open System.IO
open System.IO.Compression
open Expecto

module ExampleE2E =
  open Harness

  let private exampleProject =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Examples", "src", "EffSharp.Examples.fsproj")

  let private exampleDirectory =
    Path.GetDirectoryName(exampleProject)

  let private exampleProjectText () =
    File.ReadAllText(exampleProject)

  let private exampleObjDirectory =
    Path.Combine(exampleDirectory, "obj")

  let private generatedDirectory =
    Path.Combine(exampleObjDirectory, "Debug", "net10.0", "Gen")

  let private cleanupDirectory path =
    try
      if Directory.Exists(path) then
        Directory.Delete(path, true)
    with :? DirectoryNotFoundException ->
      ()

  let private builtExample =
    lazy (
      task {
        cleanupDirectory generatedDirectory
        return! buildProject exampleProject
      })

  let tests =
    testSequenced <| testList "ExampleE2E" [
      testTask "example project builds as a Gen consumer without manual MSBuild imports" {
        let projectText = exampleProjectText ()

        Expect.isFalse (projectText.Contains("EffSharp.Gen.props")) "the example should not manually import EffSharp.Gen.props"
        Expect.isFalse (projectText.Contains("EffSharp.Gen.targets")) "the example should not manually import EffSharp.Gen.targets"

        let! result = builtExample.Value

        Expect.equal result.ExitCode 0 $"example project should build successfully once Gen consumer wiring exists. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"example project should emit generated files into {generatedDirectory}"

        let generatedFiles =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort

        Expect.equal generatedFiles.Length 0 "the example should not emit local generated files because it declares no [<Effect>] types"
      }

      testTask "example project executes generated wrappers at runtime" {
        let! buildResult = builtExample.Value
        Expect.equal buildResult.ExitCode 0 $"example project should build successfully before runtime verification. Output:{System.Environment.NewLine}{buildResult.Output}"

        let! runResult = runBuiltProject exampleProject
        Expect.equal runResult.ExitCode 0 $"example project should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "starting program at " "the example should print the current start time"
        Expect.stringContains runResult.Output "MYVAR:" "the example should print the environment lookup result"
        Expect.stringContains runResult.Output "random val: " "the example should print the random value"
      }

      testTask "packed package includes the generator assembly and transitive MSBuild assets" {
        let packageOutputDirectory =
          Path.Combine(Path.GetTempPath(), $"effectgen-pack-{Guid.NewGuid():N}")

        cleanupDirectory packageOutputDirectory
        Directory.CreateDirectory(packageOutputDirectory) |> ignore

        let effectGenProject =
          Path.Combine(__SOURCE_DIRECTORY__, "..", "src", "Gen.fsproj")

        let! result = packProject effectGenProject packageOutputDirectory

        Expect.equal result.ExitCode 0 $"Gen should pack successfully for consumer use. Output:{System.Environment.NewLine}{result.Output}"

        let packagePath =
          Directory.GetFiles(packageOutputDirectory, "*.nupkg")
          |> Array.exactlyOne

        use archive = ZipFile.OpenRead(packagePath)
        let entries = archive.Entries |> Seq.map _.FullName |> Set.ofSeq

        Expect.isTrue (entries.Contains("lib/net10.0/Gen.dll")) "the package should include the Gen assembly"
        Expect.isTrue (entries.Contains("buildTransitive/EffSharp.Gen.props")) "the package should include the transitive props file"
        Expect.isTrue (entries.Contains("buildTransitive/EffSharp.Gen.targets")) "the package should include the transitive targets file"
      }
    ]
