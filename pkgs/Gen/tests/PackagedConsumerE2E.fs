namespace EffSharp.Gen.Tests

open System
open System.IO
open Expecto

module PackagedConsumerE2E =
  open Harness

  let private fixtureName = "PackagedConsumer"

  let private fixtureDirectory =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName)

  let private fixtureProject =
    Path.Combine(fixtureDirectory, $"{fixtureName}.fsproj")

  let private generatedDirectory =
    Path.Combine(fixtureDirectory, "obj", "Debug", "net10.0", "Gen")

  let private cleanupDirectory path =
    try
      if Directory.Exists(path) then
        Directory.Delete(path, true)
    with :? DirectoryNotFoundException ->
      ()

  let tests =
    testSequenced <| testList "PackagedConsumerE2E" [
      testTask "packed Gen package restores, builds, and runs in a consumer fixture" {
        let packageOutputDirectory =
          Path.Combine(Path.GetTempPath(), $"effectgen-package-source-{Guid.NewGuid():N}")
        let packageCacheDirectory =
          Path.Combine(Path.GetTempPath(), $"effectgen-package-cache-{Guid.NewGuid():N}")

        cleanupDirectory packageOutputDirectory
        cleanupDirectory packageCacheDirectory
        cleanupDirectory generatedDirectory
        Directory.CreateDirectory(packageOutputDirectory) |> ignore
        Directory.CreateDirectory(packageCacheDirectory) |> ignore

        let effectGenProject =
          Path.Combine(__SOURCE_DIRECTORY__, "..", "src", "Gen.fsproj")

        let! packResult = packProject effectGenProject packageOutputDirectory
        Expect.equal packResult.ExitCode 0 $"Gen should pack successfully before the packaged consumer build. Output:{System.Environment.NewLine}{packResult.Output}"

        let restoreSourceArg = $"-p:RestoreAdditionalProjectSources=\"{packageOutputDirectory}\""
        let restorePackagesArg = $"-p:RestorePackagesPath=\"{packageCacheDirectory}\""

        let! buildResult = buildProjectWithArgs fixtureProject [ restoreSourceArg; restorePackagesArg ]
        Expect.equal buildResult.ExitCode 0 $"fixture {fixtureName} should build successfully against the packed Gen package. Output:{System.Environment.NewLine}{buildResult.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"fixture {fixtureName} should emit generated files into {generatedDirectory}"

        let! runResult = runBuiltFunction fixtureProject "PackagedConsumer.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully after building against the packed Gen package. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "Hello, packaged consumer." "the packaged consumer should execute the generated wrapper at runtime"
      }
    ]
