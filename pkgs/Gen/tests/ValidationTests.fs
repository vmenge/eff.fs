namespace EffSharp.Gen.Tests

open System.IO
open System.Xml.Linq
open Expecto
open EffSharp.Gen

module ValidationTests =
  let private repoRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", ".."))

  let private coreDll =
    Path.Combine(repoRoot, "pkgs", "Core", "src", "bin", "Debug", "net10.0", "Core.dll")

  let private genDll =
    Path.Combine(repoRoot, "pkgs", "Gen", "src", "bin", "Debug", "net10.0", "Gen.dll")

  let private fixtureDirectory fixtureName =
    Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", fixtureName)

  let private interfaceFilePath fixtureName =
    Path.Combine(fixtureDirectory fixtureName, "Interfaces.fs")

  let private fixtureCompileFiles fixtureName =
    let projectPath = Path.Combine(fixtureDirectory fixtureName, $"{fixtureName}.fsproj")
    let document = XDocument.Load(projectPath)

    document.Descendants()
    |> Seq.filter (fun element -> element.Name.LocalName = "Compile")
    |> Seq.choose (fun element ->
      match element.Attribute(XName.Get "Include") with
      | null -> None
      | attribute ->
          Some(Path.GetFullPath(Path.Combine(fixtureDirectory fixtureName, attribute.Value))))
    |> Seq.toArray

  let private validationSourceFiles fixtureName =
    let compileFiles = fixtureCompileFiles fixtureName
    let interfaceFile = interfaceFilePath fixtureName
    let interfaceIndex = compileFiles |> Array.findIndex ((=) interfaceFile)
    compileFiles[0 .. interfaceIndex]

  let private validationCommandLineArgs extraArgs =
    FcsParsing.defaultCommandLineArgs
    @ extraArgs
    @ [ $"--reference:{coreDll}"; $"--reference:{genDll}" ]

  let private parsedFiles fixtureName extraArgs =
    let sourceFiles = validationSourceFiles fixtureName
    let commandLineArgs = validationCommandLineArgs extraArgs

    sourceFiles
    |> Array.map (fun filePath ->
      FcsParsing.parseFile (Array.toList sourceFiles) commandLineArgs filePath)

  let private diagnosticsFor fixtureName =
    parsedFiles fixtureName []
    |> Validation.validateFiles
    |> _.Diagnostics

  let private validationFor fixtureName =
    parsedFiles fixtureName []
    |> Validation.validateFiles

  let tests =
    testList "ValidationTests" [
      testCase "conditional compilation discovery honors project define constants" (fun () ->
        let withoutDefine =
          validationFor "ConditionalDiscovery"

        Expect.equal withoutDefine.Interfaces.Length 0 "without the project define the conditional effect interface should be absent"

        let withDefine =
          parsedFiles "ConditionalDiscovery" [ "--define:EFFECTGEN_DISCOVERY" ]
          |> Validation.validateFiles

        Expect.equal withDefine.Diagnostics.Length 0 "with the project define the conditional effect interface should validate cleanly"
        Expect.equal withDefine.Interfaces.Length 1 "with the project define the conditional effect interface should be discovered"
      )

      testCase "member kind diagnostics include a stable code and source location" (fun () ->
        let diagnostics = diagnosticsFor "UnsupportedMemberKind"

        Expect.equal diagnostics.Length 1 "the unsupported member fixture should produce exactly one validation diagnostic"

        let diagnostic = List.head diagnostics
        Expect.equal diagnostic.Code "EFFGEN002" "unsupported member kind should use the stable diagnostic code"
        Expect.equal diagnostic.Line 7 "the diagnostic should point at the offending abstract property line"
        Expect.equal diagnostic.Column 12 "the diagnostic should point at the offending member column"
      )

      testCase "duplicate generated names are reported for each conflicting interface" (fun () ->
        let diagnostics = diagnosticsFor "DuplicateGeneratedNames"

        Expect.equal diagnostics.Length 2 "both conflicting interfaces should receive collision diagnostics"
        diagnostics |> List.iter (fun diagnostic -> Expect.equal diagnostic.Code "EFFGEN005" "duplicate name collisions should use the stable diagnostic code")
      )

      testCase "abstract classes marked with [<Effect>] are rejected with EFFGEN001" (fun () ->
        let diagnostics = diagnosticsFor "InvalidAbstractClassTarget"

        Expect.equal diagnostics.Length 1 "the abstract class fixture should produce exactly one validation diagnostic"
        Expect.equal diagnostics.Head.Code "EFFGEN001" "abstract classes should be rejected as non-interface effect targets"
      )

      testCase "qualified supported return types keep their intended classifications" (fun () ->
        let validated = validationFor "QualifiedReturnTypes"

        Expect.equal validated.Diagnostics.Length 0 "qualified supported return types should validate cleanly"

        let methodsByInterface =
          validated.Interfaces
          |> List.map (fun effectInterface -> effectInterface.ServiceName, effectInterface.Methods)
          |> Map.ofList

        let methodShape interfaceName methodName =
          methodsByInterface
          |> Map.find interfaceName
          |> List.find (fun methodModel -> methodModel.SourceName = methodName)
          |> _.ReturnShape

        Expect.equal (methodShape "IParser" "Parse") (ReturnShape.Result("int", "QualifiedReturnTypesRed.ParseError")) "qualified Result should classify as Result"
        Expect.equal (methodShape "IHttp" "Fetch") (ReturnShape.Task("QualifiedReturnTypesRed.Response")) "qualified Task should classify as Task"
        Expect.equal (methodShape "IHttp" "TryFetch") (ReturnShape.TaskResult("QualifiedReturnTypesRed.Response", "QualifiedReturnTypesRed.HttpError")) "qualified Task<Result<_,_>> should classify as TaskResult"
        Expect.equal (methodShape "IStore" "Load") (ReturnShape.Async("QualifiedReturnTypesRed.Model")) "qualified Async should classify as Async"
        Expect.equal (methodShape "IStore" "TryLoad") (ReturnShape.AsyncResult("QualifiedReturnTypesRed.Model", "QualifiedReturnTypesRed.StoreError")) "qualified Async<Result<_,_>> should classify as AsyncResult"
        Expect.equal (methodShape "IFileSystem" "Read") (ReturnShape.ValueTask("string")) "qualified ValueTask should classify as ValueTask"
        Expect.equal (methodShape "IFileSystem" "TryRead") (ReturnShape.ValueTaskResult("string", "QualifiedReturnTypesRed.FileError")) "qualified ValueTask<Result<_,_>> should classify as ValueTaskResult"
        Expect.equal (methodShape "IRuntime" "Spawn") (ReturnShape.Eff("QualifiedReturnTypesRed.JobHandle<QualifiedReturnTypesRed.JobResult>", "QualifiedReturnTypesRed.SpawnError", "unit")) "qualified Eff should classify as Eff"
      )
    ]
