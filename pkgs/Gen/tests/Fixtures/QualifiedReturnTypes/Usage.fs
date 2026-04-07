namespace QualifiedReturnTypesRed

open EffSharp.Core

module Usage =
  let parseProgram () : Eff<int, ParseError, #IParser> = IParser.parse "42"
  let fetchProgram () : Eff<Response, exn, #IHttp> = IHttp.fetch "/users"
  let tryFetchProgram () : Eff<Response, HttpError, #IHttp> = IHttp.tryFetch "/users"
  let loadProgram () : Eff<Model, exn, #IStore> = IStore.load "42"
  let tryLoadProgram () : Eff<Model, StoreError, #IStore> = IStore.tryLoad "42"
  let readProgram () : Eff<string, exn, #IFileSystem> = IFileSystem.read "file.txt"
  let tryReadProgram () : Eff<string, FileError, #IFileSystem> = IFileSystem.tryRead "file.txt"
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #IRuntime> = IRuntime.spawn { Id = "job-1" }
