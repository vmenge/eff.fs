namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module DiagnosticsE2E =
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

  let private assertGeneratorDiagnostic fixtureName (expectedFragments: string list) = task {
    cleanupGeneratedDirectory fixtureName

    let! result = buildProject (fixtureProject fixtureName)

    Expect.notEqual result.ExitCode 0 $"fixture {fixtureName} should fail for a generator diagnostic"
    Expect.isFalse
      (result.Output.Contains("MSB9008")
       || result.Output.Contains("does not exist")
       || result.Output.Contains("The namespace or module 'EffSharp' is not defined")
       || result.Output.Contains("The type 'Effect' is not defined"))
      $"fixture {fixtureName} should fail because of generator validation, not broken fixture plumbing"

    for expectedFragment in expectedFragments do
      Expect.stringContains result.Output expectedFragment $"fixture {fixtureName} should include diagnostic fragment '{expectedFragment}'"
  }

  let tests =
    testList "DiagnosticsE2E" [
      testTask "attribute on non-interface target fails with a precise diagnostic" {
        do! assertGeneratorDiagnostic "InvalidAttributeTarget" [ "EFFGEN001"; "[<Effect>] can only be applied to interfaces"; "BadRecord" ]
      }

      testTask "attribute on abstract class target fails with the interface-only diagnostic" {
        do! assertGeneratorDiagnostic "InvalidAbstractClassTarget" [ "EFFGEN001"; "[<Effect>] can only be applied to interfaces"; "BadService" ]
      }

      testTask "unsupported interface member kind fails with a precise diagnostic" {
        do! assertGeneratorDiagnostic "UnsupportedMemberKind" [ "EFFGEN002"; "Only abstract methods are supported"; "Name" ]
      }

      testTask "unsupported return shape fails with a precise diagnostic" {
        do! assertGeneratorDiagnostic "UnsupportedReturnShape" [ "EFFGEN003"; "spawn"; "environment adaptation to 'Gen.Fixtures.UnsupportedReturnShape.NeededEnv' is not mechanically derivable" ]
      }

      testTask "duplicate generated names fail with a precise diagnostic" {
        do! assertGeneratorDiagnostic "DuplicateGeneratedNames" [ "EFFGEN005"; "ELogger"; "Logger"; "ILogger"; "Logger" ]
      }

      testTask "invalid source form fails with a precise diagnostic" {
        do! assertGeneratorDiagnostic "InvalidSourceForm" [ "EFFGEN004"; "IEmpty"; "must declare at least one abstract method" ]
      }
    ]
