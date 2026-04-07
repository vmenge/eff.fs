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

        Expect.stringContains generatedText "let parse (arg1: string) : EffSharp.Core.Eff<int, QualifiedReturnTypesRed.ParseError, #EParser>" "qualified Result should stay fully qualified in generated output"
        Expect.stringContains generatedText "let fetch (arg1: string) : EffSharp.Core.Eff<QualifiedReturnTypesRed.Response, 'e, #EHttp>" "qualified Task should stay fully qualified in generated output"
        Expect.stringContains generatedText "let tryFetch (arg1: string) : EffSharp.Core.Eff<QualifiedReturnTypesRed.Response, QualifiedReturnTypesRed.HttpError, #EHttp>" "qualified Task<Result<_,_>> should stay fully qualified in generated output"
        Expect.stringContains generatedText "let load (arg1: string) : EffSharp.Core.Eff<QualifiedReturnTypesRed.Model, 'e, #EStore>" "qualified Async should stay fully qualified in generated output"
        Expect.stringContains generatedText "let tryLoad (arg1: string) : EffSharp.Core.Eff<QualifiedReturnTypesRed.Model, QualifiedReturnTypesRed.StoreError, #EStore>" "qualified Async<Result<_,_>> should stay fully qualified in generated output"
        Expect.stringContains generatedText "let read (arg1: string) : EffSharp.Core.Eff<string, 'e, #EFileSystem>" "qualified ValueTask should stay fully qualified in generated output"
        Expect.stringContains generatedText "let tryRead (arg1: string) : EffSharp.Core.Eff<string, QualifiedReturnTypesRed.FileError, #EFileSystem>" "qualified ValueTask<Result<_,_>> should stay fully qualified in generated output"
        Expect.stringContains generatedText "let spawn (arg1: QualifiedReturnTypesRed.Job) : EffSharp.Core.Eff<QualifiedReturnTypesRed.JobHandle<QualifiedReturnTypesRed.JobResult>, QualifiedReturnTypesRed.SpawnError, #ERuntime>" "qualified Eff should stay fully qualified in generated output"
        Expect.stringContains generatedText "|> Eff.bind Eff.ofResult" "result normalization should remain unchanged"
        Expect.stringContains generatedText "|> Eff.bind (fun taskValue -> Eff.ofTask (fun () -> taskValue))" "task normalization should remain unchanged"
        Expect.stringContains generatedText "|> Eff.bind (fun asyncValue -> Eff.ofAsync (fun () -> asyncValue))" "async normalization should remain unchanged"
        Expect.stringContains generatedText "|> Eff.bind (fun valueTaskValue -> Eff.ofValueTask (fun () -> valueTaskValue))" "value task normalization should remain unchanged"
        Expect.isFalse (generatedText.Contains("open System")) "generated files should not rely on copied source opens"
        Expect.isFalse (generatedText.Contains("open Microsoft.FSharp.Core")) "generated files should not replay consumer opens just to make types resolve"
        Expect.stringContains generatedText "|> Eff.flatten" "qualified Eff should still flatten nested Eff values"
      }
    ]
