namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module ParseOptionsE2E =
  open Harness

  let private fixtureName = "ConditionalDiscovery"

  let private fixtureDirectory =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName)

  let private fixtureProject =
    Path.Combine(fixtureDirectory, $"{fixtureName}.fsproj")

  let private intermediateDirectory =
    Path.Combine(fixtureDirectory, "obj", "Debug", "net10.0")

  let private generatedDirectory =
    Path.Combine(intermediateDirectory, "Gen")

  let private cleanupIntermediateDirectory () =
    try
      if Directory.Exists(intermediateDirectory) then
        Directory.Delete(intermediateDirectory, true)
    with :? DirectoryNotFoundException ->
      ()

  let tests =
    testSequenced <| testList "ParseOptionsE2E" [
      testTask "generator honors project-defined conditional compilation when discovering [<Effect>] interfaces" {
        cleanupIntermediateDirectory ()

        let! result = buildProject fixtureProject

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully when Gen uses the consumer parse options. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"fixture {fixtureName} should emit generated files into {generatedDirectory}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type EGreeter =" "conditional [<Effect>] declarations enabled by the project defines should still be discovered"
        Expect.stringContains generatedText "let greet (arg1: string) : EffSharp.Core.Eff<string, 'e, #EGreeter>" "conditional [<Effect>] interfaces should generate callable wrappers"
      }
    ]
