namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module ModeCoverageE2E =
  open Harness

  let private fixtureDirectory fixtureName =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName)

  let private fixtureProject fixtureName =
    Path.Combine(fixtureDirectory fixtureName, $"{fixtureName}.fsproj")

  let private generatedDirectory fixtureName =
    Path.Combine(fixtureDirectory fixtureName, "obj", "Debug", "net10.0", "Gen")

  let private cleanupGeneratedDirectory fixtureName =
    try
      let path = generatedDirectory fixtureName

      if Directory.Exists(path) then
        Directory.Delete(path, true)
    with :? DirectoryNotFoundException ->
      ()

  let tests =
    testSequenced <| testList "ModeCoverageE2E" [
      testTask "direct mode keeps a non-interface-prefixed type name for both module and environment" {
        let fixtureName = "DirectNonInterfacePrefix"
        cleanupGeneratedDirectory fixtureName

        let! result = buildProject (fixtureProject fixtureName)

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully when direct generation keeps the tagged type name intact. Output:{System.Environment.NewLine}{result.Output}"

        let generatedText =
          Directory.GetFiles(generatedDirectory fixtureName, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type Logger with" "direct generation should emit a type extension for the tagged type"
        Expect.stringContains generatedText "static member debug (arg1: string) : EffSharp.Core.Eff<unit, 'e, #Logger>" "direct generation should use the tagged type itself as the environment constraint"
        Expect.isFalse (generatedText.Contains("type ELogger =")) "direct generation should not synthesize a wrapper interface for non-I-prefixed types"

        let! runResult = runBuiltFunction (fixtureProject fixtureName) "DirectNonInterfacePrefixRed.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "direct-non-interface-prefix-runtime-ok" "runtime verification should exercise the direct module for a non-I-prefixed type"
      }

      testTask "wrapped mode keeps source callable types while applying all wrapper naming branches" {
        let fixtureName = "WrappedNaming"
        cleanupGeneratedDirectory fixtureName

        let! result = buildProject (fixtureProject fixtureName)

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully when wrapped generation applies all naming branches. Output:{System.Environment.NewLine}{result.Output}"

        let generatedText =
          Directory.GetFiles(generatedDirectory fixtureName, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type IGreeter with" "wrapped generation should keep the source interface as the callable type for I-prefixed interfaces"
        Expect.stringContains generatedText "type EGreeter =" "wrapped generation should swap I for E when the tagged type follows the interface naming convention"
        Expect.stringContains generatedText "abstract Greeter: IGreeter" "I-prefixed wrapped generation should still expose the stripped property name"
        Expect.stringContains generatedText "static member greet (arg1: string) : EffSharp.Core.Eff<string, 'e, #EGreeter>" "wrapped generation should use the wrapper environment for I-prefixed interfaces"

        Expect.stringContains generatedText "type Logger with" "wrapped generation should keep the source type as the callable type for non-I-prefixed interfaces"
        Expect.stringContains generatedText "type ELogger =" "wrapped generation should prepend E for non-I-prefixed interfaces"
        Expect.stringContains generatedText "abstract Logger: Logger" "non-I-prefixed wrapped generation should preserve the original property name"
        Expect.stringContains generatedText "static member debug (arg1: string) : EffSharp.Core.Eff<unit, 'e, #ELogger>" "wrapped generation should use the wrapper environment for non-I-prefixed interfaces"

        Expect.stringContains generatedText "type Ilogger with" "wrapped generation should keep the source type as the callable type even for I+lowercase edge cases"
        Expect.stringContains generatedText "type EIlogger =" "wrapped generation should prepend E instead of stripping I when the second character is not uppercase"
        Expect.stringContains generatedText "abstract Ilogger: Ilogger" "I+lowercase wrapped generation should preserve the full property name"
        Expect.stringContains generatedText "static member trace (arg1: string) : EffSharp.Core.Eff<unit, 'e, #EIlogger>" "I+lowercase wrapped generation should use the prefixed wrapper interface"

        let! runResult = runBuiltFunction (fixtureProject fixtureName) "WrappedNamingRed.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "wrapped-naming-runtime-ok" "runtime verification should exercise the wrapped naming branches"
      }

      testTask "direct and wrapped modes can coexist in one project without changing callable type naming" {
        let fixtureName = "MixedModes"
        cleanupGeneratedDirectory fixtureName

        let! result = buildProject (fixtureProject fixtureName)

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully when direct and wrapped generation coexist. Output:{System.Environment.NewLine}{result.Output}"

        let generatedText =
          Directory.GetFiles(generatedDirectory fixtureName, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "type ILogger with" "direct generation should keep the source interface as the callable type in mixed-mode projects"
        Expect.stringContains generatedText "static member info (arg1: string) : EffSharp.Core.Eff<unit, 'e, #ILogger>" "direct generation should still use the tagged interface as the environment constraint in mixed-mode projects"
        Expect.isFalse (generatedText.Contains("type ELogger =")) "direct generation should not emit a wrapper interface for ILogger in mixed-mode projects"

        Expect.stringContains generatedText "type IClock with" "wrapped generation should keep the source interface as the callable type in mixed-mode projects"
        Expect.stringContains generatedText "type EClock =" "wrapped generation should still emit the wrapper interface in mixed-mode projects"
        Expect.stringContains generatedText "abstract Clock: IClock" "wrapped generation should still emit the service property in mixed-mode projects"
        Expect.stringContains generatedText "static member now () : EffSharp.Core.Eff<string, 'e, #EClock>" "wrapped generation should still use the wrapper environment in mixed-mode projects"

        let! runResult = runBuiltFunction (fixtureProject fixtureName) "MixedModesRed.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "mixed-modes-runtime-ok" "runtime verification should exercise both direct and wrapped generation in one project"
      }
    ]
