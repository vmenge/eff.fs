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
      testTask "example project builds as a Gen consumer in the same build" {
        let projectText = exampleProjectText ()

        Expect.isFalse (projectText.Contains("Gen.props")) "the example should not manually import Gen.props"
        Expect.isFalse (projectText.Contains("Gen.targets")) "the example should not manually import Gen.targets"

        let! result = builtExample.Value

        Expect.equal result.ExitCode 0 $"example project should build successfully once Gen consumer wiring exists. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"example project should emit generated files into {generatedDirectory}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type EGreeter =" "the example should generate an EGreeter environment interface"
        Expect.stringContains generatedText "let greet (arg1: string) : Eff<string, 'e, #EGreeter>" "the example should generate a callable wrapper from the [<Effect>] interface"
      }

      testTask "example project executes generated wrappers at runtime" {
        let! buildResult = builtExample.Value
        Expect.equal buildResult.ExitCode 0 $"example project should build successfully before runtime verification. Output:{System.Environment.NewLine}{buildResult.Output}"

        let! runResult = runBuiltFunction exampleProject "EffSharp.Examples.Program" "run"
        Expect.equal runResult.ExitCode 0 $"example project should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "Hello, Gen." "the example should print the greeting produced through the generated wrapper"
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
        Expect.isTrue (entries.Contains("buildTransitive/Gen.props")) "the package should include the transitive props file"
        Expect.isTrue (entries.Contains("buildTransitive/Gen.targets")) "the package should include the transitive targets file"
      }
    ]
