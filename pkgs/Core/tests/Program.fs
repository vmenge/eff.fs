namespace EffSharp.Core.Tests

module Program =
  open Expecto

  [<EntryPoint>]
  let main argv =
    runTestsWithCLIArgs
      []
      argv
      (testList "all" [ Eff.tests; CE.tests; ReportCE.tests ])
