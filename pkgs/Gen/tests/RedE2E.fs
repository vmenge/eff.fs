namespace EffSharp.Gen.Tests

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
       || result.Output.Contains("The namespace or module 'EffSharp' is not defined")
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

  let private assertMeaningfulRedFailure (fixtureName: string) (expectedFragments: string list) = task {
    let! result = buildProject (fixtureProject fixtureName)

    Expect.notEqual result.ExitCode 0 $"fixture {fixtureName} should still fail before its wave turns green"

    Expect.isFalse
      (result.Output.Contains("MSB9008")
       || result.Output.Contains("does not exist")
       || result.Output.Contains("The namespace or module 'EffSharp' is not defined")
       || result.Output.Contains("The type 'Effect' is not defined")
       || result.Output.Contains("The type 'Eff' is not defined"))
      $"fixture {fixtureName} should fail because its generation behavior is not implemented yet, not because of broken plumbing"

    for expected in expectedFragments do
      Expect.isTrue
        (result.Output.Contains(expected))
        $"fixture {fixtureName} should mention the expected unfinished behavior marker {expected}"
  }

  let tests =
    testList "RedE2E" []
