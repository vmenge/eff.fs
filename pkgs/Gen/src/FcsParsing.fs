namespace EffSharp.Gen

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type ParsedSourceFile = {
  FilePath: string
  Source: string
  ParseTree: ParsedInput
  CheckResults: FSharpCheckFileResults
}

module FcsParsing =
  let private checker = lazy (FSharpChecker.Create())
  let defaultCommandLineArgs = [ "--targetprofile:netcore" ]
  let private fullDisplayContext =
    FSharpDisplayContext.Empty.WithShortTypeNames(false)

  let private simplifyTypeText (text: string) =
    text
      .Replace("System.String", "string")
      .Replace("Microsoft.FSharp.Core.bool", "bool")
      .Replace("Microsoft.FSharp.Core.byte", "byte")
      .Replace("Microsoft.FSharp.Core.sbyte", "sbyte")
      .Replace("Microsoft.FSharp.Core.int16", "int16")
      .Replace("Microsoft.FSharp.Core.uint16", "uint16")
      .Replace("Microsoft.FSharp.Core.int", "int")
      .Replace("Microsoft.FSharp.Core.uint32", "uint32")
      .Replace("Microsoft.FSharp.Core.int64", "int64")
      .Replace("Microsoft.FSharp.Core.uint64", "uint64")
      .Replace("Microsoft.FSharp.Core.nativeint", "nativeint")
      .Replace("Microsoft.FSharp.Core.unativeint", "unativeint")
      .Replace("Microsoft.FSharp.Core.float32", "float32")
      .Replace("Microsoft.FSharp.Core.float", "float")
      .Replace("Microsoft.FSharp.Core.decimal", "decimal")
      .Replace("Microsoft.FSharp.Core.char", "char")
      .Replace("Microsoft.FSharp.Core.string", "string")
      .Replace("Microsoft.FSharp.Core.Unit", "unit")
      .Replace("Microsoft.FSharp.Core.unit", "unit")
      .Replace("Microsoft.FSharp.Core.Result<", "Result<")
      .Replace("Microsoft.FSharp.Control.Async<", "Async<")

  let renderType (typ: FSharpType) =
    typ.Format(fullDisplayContext)
    |> simplifyTypeText

  let lineText (source: string) (lineNumber: int) =
    source.Split('\n')[lineNumber - 1]

  let tryFindMemberSymbol (parsedFile: ParsedSourceFile) (ident: Ident) =
    let line = lineText parsedFile.Source ident.idRange.StartLine

    parsedFile.CheckResults.GetSymbolUseAtLocation(
      ident.idRange.StartLine,
      ident.idRange.EndColumn,
      line,
      [ ident.idText ]
    )
    |> Option.bind (fun symbolUse ->
      match symbolUse.Symbol with
      | :? FSharpMemberOrFunctionOrValue as memberSymbol -> Some memberSymbol
      | _ -> None)

  let parseFile (sourceFiles: string list) (commandLineArgs: string list) (filePath: string) =
    let source = File.ReadAllText(filePath)
    let sourceText = SourceText.ofString source
    let parsingOptions, _ = checker.Value.GetParsingOptionsFromCommandLineArgs(sourceFiles, commandLineArgs)
    let parseResults = checker.Value.ParseFile(filePath, sourceText, parsingOptions) |> Async.RunSynchronously

    let projectOptions =
      checker.Value.GetProjectOptionsFromCommandLineArgs(
        filePath,
        Array.ofList (commandLineArgs @ sourceFiles)
      )

    let _, checkAnswer =
      checker.Value.ParseAndCheckFileInProject(filePath, 0, sourceText, projectOptions)
      |> Async.RunSynchronously

    let checkResults =
      match checkAnswer with
      | FSharpCheckFileAnswer.Succeeded results -> results
      | FSharpCheckFileAnswer.Aborted -> failwith $"semantic type checking aborted for {filePath}"

    {
      FilePath = filePath
      Source = source
      ParseTree = parseResults.ParseTree
      CheckResults = checkResults
    }
