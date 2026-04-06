namespace EffFs.EffectGen.Tests

open System.IO
open Expecto
open EffFs.EffectGen

module ValidationTests =
  let private fixturePath fixtureName =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName, "Interfaces.fs")

  let private diagnosticsFor fixtureName =
    [ fixturePath fixtureName |> FcsParsing.parseFile ]
    |> Validation.validateFiles
    |> _.Diagnostics

  let tests =
    testList "ValidationTests" [
      testCase "member kind diagnostics include a stable code and source location" (fun () ->
        let diagnostics = diagnosticsFor "UnsupportedMemberKind"

        Expect.equal diagnostics.Length 1 "the unsupported member fixture should produce exactly one validation diagnostic"

        let diagnostic = List.head diagnostics
        Expect.equal diagnostic.Code "EFFGEN002" "unsupported member kind should use the stable diagnostic code"
        Expect.equal diagnostic.Line 7 "the diagnostic should point at the offending abstract property line"
        Expect.equal diagnostic.Column 12 "the diagnostic should point at the offending member column"
      )

      testCase "duplicate generated names are reported for each conflicting interface" (fun () ->
        let diagnostics = diagnosticsFor "DuplicateGeneratedNames"

        Expect.equal diagnostics.Length 2 "both conflicting interfaces should receive collision diagnostics"
        diagnostics |> List.iter (fun diagnostic -> Expect.equal diagnostic.Code "EFFGEN005" "duplicate name collisions should use the stable diagnostic code")
      )
    ]
