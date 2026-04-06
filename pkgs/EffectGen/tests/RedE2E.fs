namespace EffFs.EffectGen.Tests

open System.IO
open Expecto

module RedE2E =
  open Harness

  let private fixtureProject fixtureName =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName, $"{fixtureName}.fsproj")

  let private assertMissingGeneratedSurface (fixtureName: string) (expectedSymbols: string list) = task {
    let! result = buildProject (fixtureProject fixtureName)

    Expect.notEqual result.ExitCode 0 $"fixture {fixtureName} should fail before generation exists"

    Expect.isFalse
      (result.Output.Contains("MSB9008")
       || result.Output.Contains("does not exist")
       || result.Output.Contains("The namespace or module 'EffFs' is not defined")
       || result.Output.Contains("The type 'Effect' is not defined")
       || result.Output.Contains("The type 'Eff' is not defined"))
      $"fixture {fixtureName} should have valid plumbing; the red state should come from missing generated wrappers"

    for expected in expectedSymbols do
      Expect.isTrue
        (result.Output.Contains(expected : string))
        $"fixture {fixtureName} should mention missing generated surface {expected}"

    Expect.isTrue
      (result.Output.Contains("not defined") || result.Output.Contains("is undefined"))
      $"fixture {fixtureName} should fail because generated surfaces are missing"
  }

  let tests =
    testList "RedE2E" [
      testTask "supported async matrix fixture is red because generated wrappers are missing" {
        do! assertMissingGeneratedSurface "SupportedAsync" [ "EHttp"; "EStore"; "EFileSystem" ]
      }

      testTask "supported Eff exact fixture is red because generated wrappers are missing" {
        do! assertMissingGeneratedSurface "SupportedEffExact" [ "ERuntime" ]
      }

      testTask "supported Eff provideFrom fixture is red because generated wrappers are missing" {
        do! assertMissingGeneratedSurface "SupportedEffProvideFrom" [ "ERuntimeService" ]
      }
    ]
