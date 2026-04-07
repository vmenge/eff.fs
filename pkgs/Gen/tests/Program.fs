open Expecto
open EffSharp.Gen.Tests

[<EntryPoint>]
let main argv =
  runTestsWithCLIArgs
    []
    argv
    (testSequenced <| testList
        "all"
        [
          HarnessTests.tests
          ScaffoldTests.tests
          RedE2E.tests
          ParseOptionsE2E.tests
          ModeCoverageE2E.tests
          SupportedSyncE2E.tests
          SupportedAsyncE2E.tests
          QualifiedReturnTypesE2E.tests
          SupportedEffExactE2E.tests
          SupportedEffProvideFromE2E.tests
          DiagnosticsE2E.tests
          PackagedConsumerE2E.tests
          RegressionE2E.tests
          ValidationTests.tests
          ExampleE2E.tests
        ]
    )
