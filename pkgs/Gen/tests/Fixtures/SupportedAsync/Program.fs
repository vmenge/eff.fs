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
  interface EHttp with
    member _.Http = http

  interface EStore with
    member _.Store = store

  interface EFileSystem with
    member _.FileSystem = fileSystem

let private expectOk expected exit name =
  match exit with
  | Exit.Ok value when value = expected -> ()
  | Exit.Ok value -> failwithf "%s returned %A instead of %A" name value expected
  | Exit.Err err -> failwithf "%s returned managed error %A" name err
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
