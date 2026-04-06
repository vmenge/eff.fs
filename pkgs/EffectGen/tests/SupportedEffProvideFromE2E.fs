namespace EffFs.EffectGen.Tests

open System.IO
open Expecto

module SupportedEffProvideFromE2E =
  open Harness

  let private fixtureName = "SupportedEffProvideFrom"

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

  let private builtFixture =
    lazy (
      task {
        cleanupGeneratedDirectory ()
        return! buildProject fixtureProject
      })

  let tests =
    testSequenced <| testList "SupportedEffProvideFromE2E" [
      testTask "supported Eff provideFrom fixture builds with generated wrappers in the same build" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully once mechanical Eff adaptation exists. Output:{System.Environment.NewLine}{result.Output}"
      }

      testTask "supported Eff provideFrom generated output upcasts through provideFrom before flattening" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully before inspecting generated output. Output:{System.Environment.NewLine}{result.Output}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type ERuntimeService =" "The generated environment interface should exist for the service"
        Expect.stringContains generatedText "inherit IRuntimeEnv" "The generated environment interface should inherit the mechanically matching inner environment"
        Expect.stringContains generatedText "|> Eff.map (Eff.provideFrom (fun (outer: #ERuntimeService) -> outer :> IRuntimeEnv))" "The nested Eff should be adapted through a direct upcast before flattening"
        Expect.stringContains generatedText "|> Eff.flatten" "The adapted nested Eff should then be flattened"
      }

      testTask "supported Eff provideFrom fixture executes generated wrappers at runtime" {
        let! buildResult = builtFixture.Value
        Expect.equal buildResult.ExitCode 0 $"fixture {fixtureName} should build successfully before runtime verification. Output:{System.Environment.NewLine}{buildResult.Output}"

        let! runResult = runBuiltExpression fixtureProject "SupportedEffProvideFromRed.Program.run ()"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "supported-eff-providefrom-runtime-ok" "runtime verification should exercise the generated provideFrom wrapper"
      }
    ]
