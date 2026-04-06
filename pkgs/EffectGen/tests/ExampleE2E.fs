namespace EffFs.EffectGen.Tests

open System
open System.IO
open System.IO.Compression
open Expecto

module ExampleE2E =
  open Harness

  let private exampleProject =
    Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "Examples", "src", "EffFs.Examples.fsproj")

  let private exampleDirectory =
    Path.GetDirectoryName(exampleProject)

  let private exampleObjDirectory =
    Path.Combine(exampleDirectory, "obj")

  let private generatedDirectory =
    Path.Combine(exampleObjDirectory, "Debug", "net10.0", "EffectGen")

  let private cleanupDirectory path =
    try
      if Directory.Exists(path) then
        Directory.Delete(path, true)
    with :? DirectoryNotFoundException ->
      ()

  let tests =
    testSequenced <| testList "ExampleE2E" [
      testTask "example project builds as an EffectGen consumer in the same build" {
        cleanupDirectory exampleObjDirectory

        let! result = buildProject exampleProject

        Expect.equal result.ExitCode 0 $"example project should build successfully once EffectGen consumer wiring exists. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"example project should emit generated files into {generatedDirectory}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type EGreeter =" "the example should generate an EGreeter environment interface"
        Expect.stringContains generatedText "let greet (arg1: string) : Eff<string, 'e, #EGreeter>" "the example should generate a callable wrapper from the [<Effect>] interface"
      }

      testTask "packed package includes the generator assembly and transitive MSBuild assets" {
        let packageOutputDirectory =
          Path.Combine(Path.GetTempPath(), $"effectgen-pack-{Guid.NewGuid():N}")

        cleanupDirectory packageOutputDirectory
        Directory.CreateDirectory(packageOutputDirectory) |> ignore

        let effectGenProject =
          Path.Combine(__SOURCE_DIRECTORY__, "..", "src", "EffectGen.fsproj")

        let! result = packProject effectGenProject packageOutputDirectory

        Expect.equal result.ExitCode 0 $"EffectGen should pack successfully for consumer use. Output:{System.Environment.NewLine}{result.Output}"

        let packagePath =
          Directory.GetFiles(packageOutputDirectory, "*.nupkg")
          |> Array.exactlyOne

        use archive = ZipFile.OpenRead(packagePath)
        let entries = archive.Entries |> Seq.map _.FullName |> Set.ofSeq

        Expect.isTrue (entries.Contains("lib/net10.0/EffectGen.dll")) "the package should include the EffectGen assembly"
        Expect.isTrue (entries.Contains("buildTransitive/EffectGen.props")) "the package should include the transitive props file"
        Expect.isTrue (entries.Contains("buildTransitive/EffectGen.targets")) "the package should include the transitive targets file"
      }
    ]
