module SupportedAsyncRed.Program

open System
open System.Threading.Tasks
open EffSharp.Core

type Http() =
  interface IHttp with
    member _.Fetch(_path: string) =
      Task.FromResult({ StatusCode = 200 })

    member _.TryFetch(_path: string) =
      Task.FromResult(Ok { StatusCode = 202 })

type Store() =
  interface IStore with
    member _.Load(id: string) =
      async { return { Id = id } }

    member _.TryLoad(id: string) =
      async { return Ok { Id = id } }

type FileSystem() =
  interface IFileSystem with
    member _.Read(_path: string) =
      ValueTask<string>("contents")

    member _.TryRead(_path: string) =
      ValueTask<Result<string, FileError>>(Ok "contents")

type AppEnv(http: IHttp, store: IStore, fileSystem: IFileSystem) =
  interface IHttp with
    member _.Fetch(path: string) = http.Fetch(path)
    member _.TryFetch(path: string) = http.TryFetch(path)

  interface IStore with
    member _.Load(id: string) = store.Load(id)
    member _.TryLoad(id: string) = store.TryLoad(id)

  interface IFileSystem with
    member _.Read(path: string) = fileSystem.Read(path)
    member _.TryRead(path: string) = fileSystem.TryRead(path)

let private expectOk expected exit name =
  match exit with
  | Exit.Ok value when value = expected -> ()
  | Exit.Ok value -> failwithf "%s returned %A instead of %A" name value expected
  | Exit.Err err -> failwithf "%s returned managed error %A" name err
  | Exit.Aborted -> failwithf "%s was aborted" name
  | Exit.Exn ex -> raise ex

let private runTaskSync env eff =
  Eff.runTask env eff
  |> fun task -> task.GetAwaiter().GetResult()

let run () =
  let env =
    AppEnv(Http() :> IHttp, Store() :> IStore, FileSystem() :> IFileSystem)

  expectOk { StatusCode = 200 } (runTaskSync env (Usage.fetchProgram ())) "fetchProgram"
  expectOk { StatusCode = 202 } (runTaskSync env (Usage.tryFetchProgram ())) "tryFetchProgram"
  expectOk { Id = "42" } (runTaskSync env (Usage.loadProgram ())) "loadProgram"
  expectOk { Id = "42" } (runTaskSync env (Usage.tryLoadProgram ())) "tryLoadProgram"
  expectOk "contents" (runTaskSync env (Usage.readProgram ())) "readProgram"
  expectOk "contents" (runTaskSync env (Usage.tryReadProgram ())) "tryReadProgram"

  "supported-async-runtime-ok"
