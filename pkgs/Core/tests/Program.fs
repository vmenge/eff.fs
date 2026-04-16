namespace EffSharp.Core.Tests

module Program =
  open Expecto

  [<EntryPoint>]
  let main argv =
    runTestsWithCLIArgs
      []
      argv
      (testList "all" [ Eff.tests; Concurrency.tests; CE.tests; ReportCE.tests ])
