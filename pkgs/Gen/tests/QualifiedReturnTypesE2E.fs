namespace EffSharp.Gen.Tests

open System.IO
open Expecto

module QualifiedReturnTypesE2E =
  open Harness

  let private fixtureName = "QualifiedReturnTypes"

  let private fixtureDirectory =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName)

  let private fixtureProject =
    Path.Combine(fixtureDirectory, $"{fixtureName}.fsproj")

  let private intermediateDirectory =
    Path.Combine(fixtureDirectory, "obj", "Debug", "net10.0")

  let private generatedDirectory =
    Path.Combine(intermediateDirectory, "Gen")

  let private cleanupIntermediateDirectory () =
    try
      if Directory.Exists(intermediateDirectory) then
        Directory.Delete(intermediateDirectory, true)
    with :? DirectoryNotFoundException ->
      ()

  let tests =
    testSequenced <| testList "QualifiedReturnTypesE2E" [
      testTask "qualified supported return types build and normalize like unqualified forms" {
        cleanupIntermediateDirectory ()

        let! result = buildProject fixtureProject

        Expect.equal result.ExitCode 0 $"fixture {fixtureName} should build successfully when qualified supported return types are classified correctly. Output:{System.Environment.NewLine}{result.Output}"
        Expect.isTrue (Directory.Exists(generatedDirectory)) $"fixture {fixtureName} should emit generated files into {generatedDirectory}"

        let generatedText =
          Directory.GetFiles(generatedDirectory, "*.g.fs")
          |> Array.sort
          |> Array.map File.ReadAllText
          |> String.concat System.Environment.NewLine

        Expect.stringContains generatedText "let parse (arg1: string) : Eff<int, ParseError, #EParser>" "qualified Result should classify like Result"
        Expect.stringContains generatedText "let fetch (arg1: string) : Eff<Response, 'e, #EHttp>" "qualified Task should classify like Task"
        Expect.stringContains generatedText "let tryFetch (arg1: string) : Eff<Response, HttpError, #EHttp>" "qualified Task<Result<_,_>> should classify like Task<Result<_,_>>"
        Expect.stringContains generatedText "let load (arg1: string) : Eff<Model, 'e, #EStore>" "qualified Async should classify like Async"
        Expect.stringContains generatedText "let tryLoad (arg1: string) : Eff<Model, StoreError, #EStore>" "qualified Async<Result<_,_>> should classify like Async<Result<_,_>>"
        Expect.stringContains generatedText "let read (arg1: string) : Eff<string, 'e, #EFileSystem>" "qualified ValueTask should classify like ValueTask"
        Expect.stringContains generatedText "let tryRead (arg1: string) : Eff<string, FileError, #EFileSystem>" "qualified ValueTask<Result<_,_>> should classify like ValueTask<Result<_,_>>"
        Expect.stringContains generatedText "let spawn (arg1: Job) : Eff<JobHandle<JobResult>, SpawnError, #ERuntime>" "qualified Eff should classify like Eff"
        Expect.stringContains generatedText "|> Eff.bind Eff.ofResult" "qualified Result families should still normalize through Eff.ofResult"
        Expect.stringContains generatedText "|> Eff.bind (fun taskValue -> Eff.ofTask (fun () -> taskValue))" "qualified Task should still normalize through Eff.ofTask"
        Expect.stringContains generatedText "|> Eff.bind (fun asyncValue -> Eff.ofAsync (fun () -> asyncValue))" "qualified Async should still normalize through Eff.ofAsync"
        Expect.stringContains generatedText "|> Eff.bind (fun valueTaskValue -> Eff.ofValueTask (fun () -> valueTaskValue))" "qualified ValueTask should still normalize through Eff.ofValueTask"
        Expect.stringContains generatedText "|> Eff.flatten" "qualified Eff should still flatten nested Eff values"
      }
    ]
