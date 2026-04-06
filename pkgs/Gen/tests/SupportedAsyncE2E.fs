namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module SupportedAsyncE2E =
  open Harness

  let private fixtureName = "SupportedAsync"

  let private fixtureDirectory =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName)

  let private fixtureProject =
    Path.Combine(fixtureDirectory, $"{fixtureName}.fsproj")

  let private generatedDirectory =
    Path.Combine(fixtureDirectory, "obj", "Debug", "net10.0", "Gen")

  let private cleanupGeneratedDirectory () =
    try
      if Directory.Exists(generatedDirectory) then
        Directory.Delete(generatedDirectory, true)
    with :? DirectoryNotFoundException ->
      ()

  let private builtFixture =
    lazy (
      task {
        cleanupGeneratedDirectory ()
        return! buildProject fixtureProject
      })

  let tests =
    testSequenced <| testList "SupportedAsyncE2E" [
      testTask "supported async fixture builds with generated wrappers in the same build" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully once async generation exists. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"fixture {fixtureName} should emit generated files into {generatedDirectory}"
      }

      testTask "supported async generated output normalizes Task Async and ValueTask through Eff helpers" {
        let! result = builtFixture.Value

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully before inspecting generated output. Output:{System.Environment.NewLine}{result.Output}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "let fetch (arg1: string) : Eff<Response, 'e, #EHttp>" "Task-returning members should remain generic over the error channel"
        Expect.stringContains generatedText "|> Eff.bind (fun taskValue -> Eff.ofTask (fun () -> taskValue))" "Task-returning members should normalize through Eff.ofTask"
        Expect.stringContains generatedText "let tryFetch (arg1: string) : Eff<Response, HttpError, #EHttp>" "Task<Result<_,_>> should produce the concrete error channel"
        Expect.stringContains generatedText "|> Eff.bind Eff.ofResult" "Task<Result<_,_>> should finish by binding Eff.ofResult"
        Expect.stringContains generatedText "let load (arg1: string) : Eff<Model, 'e, #EStore>" "Async-returning members should remain generic over the error channel"
        Expect.stringContains generatedText "|> Eff.bind (fun asyncValue -> Eff.ofAsync (fun () -> asyncValue))" "Async-returning members should normalize through Eff.ofAsync"
        Expect.stringContains generatedText "let read (arg1: string) : Eff<string, 'e, #EFileSystem>" "ValueTask-returning members should remain generic over the error channel"
        Expect.stringContains generatedText "|> Eff.bind (fun valueTaskValue -> Eff.ofValueTask (fun () -> valueTaskValue))" "ValueTask-returning members should normalize through Eff.ofValueTask"
      }

      testTask "supported async fixture executes generated wrappers at runtime" {
        let! buildResult = builtFixture.Value
        Expect.equal buildResult.ExitCode 0 $"fixture {fixtureName} should build successfully before runtime verification. Output:{System.Environment.NewLine}{buildResult.Output}"

        let! runResult = runBuiltFunction fixtureProject "SupportedAsyncRed.Program" "run"
        Expect.equal runResult.ExitCode 0 $"fixture {fixtureName} should run successfully. Output:{System.Environment.NewLine}{runResult.Output}"
        Expect.stringContains runResult.Output "supported-async-runtime-ok" "runtime verification should exercise the generated async wrappers"
      }
    ]
