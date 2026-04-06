namespace EffFs.EffectGen.Tests

open System.IO
open Expecto

module SupportedSyncE2E =
  open Harness

  let private fixtureName = "SupportedSync"

  let private fixtureDirectory =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName)

  let private fixtureProject =
    Path.Combine(fixtureDirectory, $"{fixtureName}.fsproj")

  let private generatedDirectory =
    Path.Combine(fixtureDirectory, "obj", "Debug", "net10.0", "EffectGen")

  let private cleanupGeneratedDirectory () =
    try
      if Directory.Exists(generatedDirectory) then
        Directory.Delete(generatedDirectory, true)
    with :? DirectoryNotFoundException ->
      ()

  let tests =
    testSequenced <| testList "SupportedSyncE2E" [
      testTask "supported sync fixture builds with generated wrappers in the same build" {
        cleanupGeneratedDirectory ()

        let! result = buildProject fixtureProject

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully once sync generation exists. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"fixture {fixtureName} should emit generated files into {generatedDirectory}"
      }

      testTask "supported sync generated output uses naming and result normalization from the spec" {
        cleanupGeneratedDirectory ()

        let! result = buildProject fixtureProject

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully before inspecting generated output. Output:{System.Environment.NewLine}{result.Output}"

        let generatedFiles =
          if Directory.Exists(generatedDirectory) then
            Directory.GetFiles(generatedDirectory, "*.g.fs")
          else
            [||]

        Expect.equal generatedFiles.Length 4 "supported sync should emit one generated file per marked interface"

        let generatedText =
          generatedFiles
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type ELogger =" "ILogger should produce ELogger"
        Expect.stringContains generatedText "abstract Logger: ILogger" "ILogger should produce a Logger service property"
        Expect.stringContains generatedText "let debug (arg1: string) : Eff<unit, 'e, #ELogger>" "plain unit return should stay generic over the error channel"
        Expect.stringContains generatedText "type EParser =" "IParser should produce EParser"
        Expect.stringContains generatedText "let parse (arg1: string) : Eff<int, ParseError, #EParser>" "Result-returning members should produce the concrete error channel"
        Expect.stringContains generatedText "|> Eff.bind Eff.ofResult" "Result-returning members should normalize through Eff.ofResult"
        Expect.stringContains generatedText "let tryFind (arg1: int, arg2: string) : Eff<User, LookupError, #ELookup>" "tupled members should preserve the tuple structure in the generated wrapper"
      }
    ]
