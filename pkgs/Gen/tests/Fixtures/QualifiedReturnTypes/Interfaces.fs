namespace QualifiedReturnTypesRed

open EffSharp.Gen

[<Effect>]
type IParser =
  abstract Parse: string -> Microsoft.FSharp.Core.Result<int, ParseError>

[<Effect>]
type IHttp =
  abstract Fetch: string -> System.Threading.Tasks.Task<Response>
  abstract TryFetch: string -> System.Threading.Tasks.Task<Microsoft.FSharp.Core.Result<Response, HttpError>>

[<Effect>]
type IStore =
  abstract Load: string -> Microsoft.FSharp.Control.Async<Model>
  abstract TryLoad: string -> Microsoft.FSharp.Control.Async<Microsoft.FSharp.Core.Result<Model, StoreError>>

[<Effect>]
type IFileSystem =
  abstract Read: string -> System.Threading.Tasks.ValueTask<string>
  abstract TryRead: string -> System.Threading.Tasks.ValueTask<Microsoft.FSharp.Core.Result<string, FileError>>

[<Effect>]
type IRuntime =
  abstract Spawn: Job -> EffSharp.Core.Eff<JobHandle<JobResult>, SpawnError, unit>
