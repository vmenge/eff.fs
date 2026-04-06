namespace EffFs.EffectGen

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type ParsedSourceFile = {
  FilePath: string
  Source: string
  ParseTree: ParsedInput
}

module FcsParsing =
  let private checker = lazy (FSharpChecker.Create())

  let parseFile (filePath: string) =
    let source = File.ReadAllText(filePath)
    let sourceText = SourceText.ofString source
    let parsingOptions, _ = checker.Value.GetParsingOptionsFromCommandLineArgs([ filePath ], [ "--targetprofile:netcore" ])
    let results = checker.Value.ParseFile(filePath, sourceText, parsingOptions) |> Async.RunSynchronously

    {
      FilePath = filePath
      Source = source
      ParseTree = results.ParseTree
    }
