namespace EffSharp.Gen.Tests

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
    Path.Combine(fixtureDirectory, "obj", "Debug", "net10.0", "Gen")

  let private cleanupGeneratedDirectory () =
    try
      if Directory.Exists(generatedDirectory) then
        Directory.Delete(generatedDirectory, true)
    with :? DirectoryNotFoundException ->
      ()

  let private builtFixture =
    lazy (
      task {
        cleanupGeneratedDirectory ()
        return! buildProject fixtureProject
      })

  let tests =
    testSequenced <| testList "SupportedSyncE2E" [
      testTask "supported sync fixture builds with generated modules in the same build" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully once sync generation exists. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"fixture {fixtureName} should emit generated files into {generatedDirectory}"
      }

      testTask "supported sync generated output uses direct modules and result normalization from the spec" {
        let! result = builtFixture.Value

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

        Expect.stringContains generatedText "type ILogger with" "ILogger should produce a type extension on the source interface"
        Expect.stringContains generatedText "static member debug (arg1: string) : EffSharp.Core.Eff<unit, 'e, #ILogger>" "plain unit return should stay generic over the error channel"
        Expect.stringContains generatedText "type IParser with" "IParser should produce a type extension on the source interface"
        Expect.stringContains generatedText "static member parse (arg1: string) : EffSharp.Core.Eff<int, SupportedSyncRed.ParseError, #IParser>" "Result-returning members should produce the concrete error channel"
        Expect.stringContains generatedText "|> Eff.bind Eff.ofResult" "Result-returning members should normalize through Eff.ofResult"
        Expect.stringContains generatedText "static member tryFind (arg1: int, arg2: string) : EffSharp.Core.Eff<SupportedSyncRed.User, SupportedSyncRed.LookupError, #ILookup>" "tupled members should preserve the tuple structure in the generated wrapper"
        Expect.isFalse (generatedText.Contains("type ELogger =")) "direct generation should not emit wrapper environment interfaces by default"
      }

      testTask "supported sync fixture executes generated modules at runtime" {
        let! buildResult = builtFixture.Value
        Expect.equal buildResult.ExitCode 0 $"fixture {fixtureName} should build successfully before runtime verification. Output:{System.Environment.NewLine}{buildResult.Output}"

        let! runResult = runBuiltFunction fixtureProject "SupportedSyncRed.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "supported-sync-runtime-ok" "runtime verification should exercise the generated sync wrappers"
      }
    ]
