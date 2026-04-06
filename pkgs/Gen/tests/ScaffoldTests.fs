namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module ScaffoldTests =
  let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", ".."))

  let private effectGenRoot =
    Path.Combine(repoRoot, "pkgs", "Gen")

  let private effectGenProject =
    Path.Combine(effectGenRoot, "src", "Gen.fsproj")

  let private solutionPath =
    Path.Combine(repoRoot, "EffSharp.slnx")

  let private readAllText path = File.ReadAllText(path)

  let tests =
    testList "Scaffold" [
      testCase "solution contains Gen projects" (fun () ->
        let solution = readAllText solutionPath

        Expect.stringContains solution "pkgs/Gen/src/Gen.fsproj" "solution should include the Gen package project"
        Expect.stringContains solution "pkgs/Gen/tool/Gen.Tool.fsproj" "solution should include the Gen source-mode tool project"
        Expect.stringContains solution "pkgs/Gen/tests/Gen.Tests.fsproj" "solution should include the Gen test project"
      )

      testCase "buildTransitive asset files exist" (fun () ->
        let propsPath = Path.Combine(effectGenRoot, "buildTransitive", "Gen.props")
        let targetsPath = Path.Combine(effectGenRoot, "buildTransitive", "Gen.targets")

        Expect.isTrue (File.Exists(propsPath)) "Gen.props should exist for transitive MSBuild wiring"
        Expect.isTrue (File.Exists(targetsPath)) "Gen.targets should exist for transitive MSBuild wiring"
      )

      testCase "package project includes buildTransitive assets for packing" (fun () ->
        let projectText = readAllText effectGenProject

        Expect.stringContains projectText "buildTransitive" "package project should reference buildTransitive assets"
        Expect.stringContains projectText "PackagePath=\"buildTransitive" "buildTransitive assets should be packed into the correct package path"
      )
    ]
