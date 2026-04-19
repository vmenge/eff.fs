namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module SupportedGenericEnvWrapE2E =
  open Harness

  let private fixtureName = "SupportedGenericEnvWrap"

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
    testSequenced <| testList "SupportedGenericEnvWrapE2E" [
      testTask "wrapped Eff methods with a generic inner environment build and flatten without adaptation" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully when wrapped Eff returns are polymorphic in their inner environment. Output:{System.Environment.NewLine}{result.Output}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "namespace SupportedGenericEnvWrapRed.Effect" "wrapped generation should emit the nested Effect namespace"
        Expect.stringContains generatedText "type Greeter =" "wrapped generation should emit the wrapper environment interface"
        Expect.stringContains generatedText "type IGreeter with" "wrapped generation should keep the source interface as the callable type"
        Expect.stringContains generatedText "static member Greet (arg1: string) : EffSharp.Core.Eff<string, Microsoft.FSharp.Core.exn, #Effect.Greeter>" "wrapped generation should expose the wrapped environment on the generated effect signature"
        Expect.stringContains generatedText "|> Eff.flatten" "generic inner Eff environments should flatten directly"
        Expect.isFalse (generatedText.Contains("|> Eff.map (Eff.provideFrom")) "generic inner Eff environments should not require provideFrom adaptation"
      }

      testTask "wrapped Eff methods with a generic inner environment execute at runtime" {
        let! buildResult = builtFixture.Value
        Expect.equal buildResult.ExitCode 0 $"fixture {fixtureName} should build successfully before runtime verification. Output:{System.Environment.NewLine}{buildResult.Output}"

        let! runResult = runBuiltFunction fixtureProject "SupportedGenericEnvWrapRed.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "supported-generic-env-wrap-runtime-ok" "runtime verification should exercise the generated wrapper"
      }
    ]
