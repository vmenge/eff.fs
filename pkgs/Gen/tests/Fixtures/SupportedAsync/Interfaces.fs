namespace SupportedAsyncRed

open System.Threading.Tasks
open EffSharp.Gen

[<Effect>]
type IHttp =
  abstract Fetch: string -> Task<Response>
  abstract TryFetch: string -> Task<Result<Response, HttpError>>

[<Effect>]
type IStore =
  abstract Load: string -> Async<Model>
  abstract TryLoad: string -> Async<Result<Model, StoreError>>

[<Effect>]
type IFileSystem =
  abstract Read: string -> ValueTask<string>
  abstract TryRead: string -> ValueTask<Result<string, FileError>>
