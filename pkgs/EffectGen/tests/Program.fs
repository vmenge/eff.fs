open Expecto
open EffFs.EffectGen.Tests

[<EntryPoint>]
let main argv =
  runTestsWithCLIArgs
    []
    argv
    (
      testList
        "all"
        [
          ScaffoldTests.tests
          RedE2E.tests
          SupportedSyncE2E.tests
          SupportedAsyncE2E.tests
          SupportedEffExactE2E.tests
          SupportedEffProvideFromE2E.tests
          DiagnosticsE2E.tests
          ValidationTests.tests
        ]
    )
