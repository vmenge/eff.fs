namespace QualifiedReturnTypesRed

open EffSharp.Core

module Usage =
  let parseProgram () : Eff<int, ParseError, #EParser> = EParser.parse "42"
  let fetchProgram () : Eff<Response, exn, #EHttp> = EHttp.fetch "/users"
  let tryFetchProgram () : Eff<Response, HttpError, #EHttp> = EHttp.tryFetch "/users"
  let loadProgram () : Eff<Model, exn, #EStore> = EStore.load "42"
  let tryLoadProgram () : Eff<Model, StoreError, #EStore> = EStore.tryLoad "42"
  let readProgram () : Eff<string, exn, #EFileSystem> = EFileSystem.read "file.txt"
  let tryReadProgram () : Eff<string, FileError, #EFileSystem> = EFileSystem.tryRead "file.txt"
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #ERuntime> = ERuntime.spawn { Id = "job-1" }
