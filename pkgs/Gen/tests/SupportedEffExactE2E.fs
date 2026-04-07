namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module SupportedEffExactE2E =
  open Harness

  let private fixtureName = "SupportedEffExact"

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
    testSequenced <| testList "SupportedEffExactE2E" [
      testTask "supported Eff exact fixture builds with generated wrappers in the same build" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully once exact Eff generation exists. Output:{System.Environment.NewLine}{result.Output}"
      }

      testTask "supported Eff exact generated output flattens nested Eff returns" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully before inspecting generated output. Output:{System.Environment.NewLine}{result.Output}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "let spawn (arg1: SupportedEffExactRed.Job) : EffSharp.Core.Eff<SupportedEffExactRed.JobHandle<SupportedEffExactRed.JobResult>, SupportedEffExactRed.SpawnError, #ERuntime>" "Eff-returning members should preserve the concrete success and error types"
        Expect.stringContains generatedText "|> Eff.flatten" "Exact Eff returns should flatten the nested Eff value"
      }

      testTask "supported Eff exact fixture executes generated wrappers at runtime" {
        let! buildResult = builtFixture.Value
        Expect.equal buildResult.ExitCode 0 $"fixture {fixtureName} should build successfully before runtime verification. Output:{System.Environment.NewLine}{buildResult.Output}"

        let! runResult = runBuiltFunction fixtureProject "SupportedEffExactRed.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "supported-eff-exact-runtime-ok" "runtime verification should exercise the generated Eff-flattening wrapper"
      }
    ]
