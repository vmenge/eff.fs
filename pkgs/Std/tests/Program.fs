namespace EffSharp.Std.Tests

open Expecto

module Program =
  [<EntryPoint>]
  let main argv =
    runTestsWithCLIArgs
      []
      argv
      (testList "all" [ Path.tests ])
