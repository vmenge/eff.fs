# EffectGen Spec

## Goal

Generate `Eff` wrappers from `[<Effect>]` interfaces so users can define effect capabilities as ordinary F# interfaces and consume them as `Eff` values inside programs and computation expressions.

The generated code must target the public `EffFs.Core.Eff` API. It must not depend on runtime internals such as `EffRuntime` or `EffNodes`.

---

## Source Shape

Input shape:

```fsharp
[<Effect>]
type ILogger =
  abstract debug: string -> unit
  abstract error: string -> unit
```

Generated shape:

```fsharp
[<Interface>]
type ELogger =
  abstract Logger: ILogger

module ELogger =
  let debug (message: string) : Eff<unit, 'e, #ELogger> =
    Eff.read (fun (env: #ELogger) -> env.Logger.debug message)

  let error (message: string) : Eff<unit, 'e, #ELogger> =
    Eff.read (fun (env: #ELogger) -> env.Logger.error message)
```

The generator creates:

- one environment interface named `E<TypeName>`
- one property on that interface exposing the original service
- one module named `E<TypeName>`
- one generated wrapper per source member

---

## Naming Rules

Given:

```fsharp
[<Effect>]
type ILogger = ...
```

Generate:

```fsharp
type ELogger =
  abstract Logger: ILogger
```

Rules:

- strip a leading `I` from interface names when building the environment interface and property name
- preserve the original service type as the property type
- emit one module with the same name as the generated environment interface
- generated wrapper function names should be idiomatic F# names derived from the source member name

Examples:

- `ILogger.debug` -> `ELogger.debug`
- `ILogger.error` -> `ELogger.error`
- `IClock.now` -> `EClock.now`

---

## Wrapper Contract

Every generated wrapper must return `Eff<_, _, _>`.

The wrapper always has the same high-level structure:

1. read the service from environment
2. call the original interface member
3. normalize the result into `Eff`
4. adapt environment when that adaptation is mechanically derivable
5. flatten when the normalized result is itself an `Eff`

The generator must never target raw `Eff` union cases directly except through the public `Eff` module.

---

## Return Normalization Rules

### Plain Return

For a plain return:

```fsharp
abstract debug: string -> unit
abstract getName: unit -> string
```

Generate:

```fsharp
let debug (message: string) : Eff<unit, 'e, #ELogger> =
  Eff.read (fun (env: #ELogger) -> env.Logger.debug message)

let getName () : Eff<string, 'e, #ELogger> =
  Eff.read (fun (env: #ELogger) -> env.Logger.getName())
```

### `Result<'t, 'e>`

For:

```fsharp
abstract parse: string -> Result<int, ParseError>
```

Generate:

```fsharp
let parse (input: string) : Eff<int, ParseError, #EParser> =
  Eff.read (fun (env: #EParser) -> env.Parser.parse input)
  |> Eff.bind Eff.ofResult
```

### `Task<'t>`

For:

```fsharp
abstract fetch: string -> Task<Response>
```

Generate:

```fsharp
let fetch (url: string) : Eff<Response, 'e, #EHttp> =
  Eff.read (fun (env: #EHttp) -> env.Http.fetch url)
  |> Eff.bind (fun t -> Eff.ofTask (fun () -> t))
```

### `Task<Result<'t, 'e>>`

For:

```fsharp
abstract tryFetch: string -> Task<Result<Response, HttpError>>
```

Generate:

```fsharp
let tryFetch (url: string) : Eff<Response, HttpError, #EHttp> =
  Eff.read (fun (env: #EHttp) -> env.Http.tryFetch url)
  |> Eff.bind (fun t -> Eff.ofTask (fun () -> t))
  |> Eff.bind Eff.ofResult
```

### `Async<'t>`

For:

```fsharp
abstract load: string -> Async<Model>
```

Generate:

```fsharp
let load (id: string) : Eff<Model, 'e, #EStore> =
  Eff.read (fun (env: #EStore) -> env.Store.load id)
  |> Eff.bind (fun a -> Eff.ofAsync (fun () -> a))
```

### `Async<Result<'t, 'e>>`

For:

```fsharp
abstract tryLoad: string -> Async<Result<Model, StoreError>>
```

Generate:

```fsharp
let tryLoad (id: string) : Eff<Model, StoreError, #EStore> =
  Eff.read (fun (env: #EStore) -> env.Store.tryLoad id)
  |> Eff.bind (fun a -> Eff.ofAsync (fun () -> a))
  |> Eff.bind Eff.ofResult
```

### `ValueTask<'t>`

For:

```fsharp
abstract read: string -> ValueTask<string>
```

Generate:

```fsharp
let read (path: string) : Eff<string, 'e, #EFileSystem> =
  Eff.read (fun (env: #EFileSystem) -> env.FileSystem.read path)
  |> Eff.bind (fun vt -> Eff.ofValueTask (fun () -> vt))
```

### `ValueTask<Result<'t, 'e>>`

For:

```fsharp
abstract tryRead: string -> ValueTask<Result<string, FileError>>
```

Generate:

```fsharp
let tryRead (path: string) : Eff<string, FileError, #EFileSystem> =
  Eff.read (fun (env: #EFileSystem) -> env.FileSystem.tryRead path)
  |> Eff.bind (fun vt -> Eff.ofValueTask (fun () -> vt))
  |> Eff.bind Eff.ofResult
```

### `Eff<'t, 'e, 'env2>`

For:

```fsharp
abstract spawn: Job -> Eff<Fiber<JobResult>, SpawnError, IRuntimeEnv>
```

Generate by reading the service call, optionally adapting env, then flattening.

If no environment adaptation is needed:

```fsharp
let spawn (job: Job) : Eff<Fiber<JobResult>, SpawnError, #ERuntime> =
  Eff.read (fun (env: #ERuntime) -> env.Runtime.spawn job)
  |> Eff.flatten
```

If environment adaptation is needed and is mechanically derivable:

```fsharp
let spawn (job: Job) : Eff<Fiber<JobResult>, SpawnError, #ERuntime> =
  Eff.read (fun (env: #ERuntime) ->
    env.Runtime.spawn job
    |> Eff.provideFrom (fun (outer: #ERuntime) -> outer :> IRuntimeEnv))
  |> Eff.flatten
```

The outer wrapper must not guess arbitrary projections. It may only emit `provideFrom` when the projection is mechanically derivable from the types, such as a direct upcast or subtype-compatible adaptation.

---

## Environment Adaptation Rules

Generated code may use `Eff.provideFrom` only when the adaptation is obvious from the type relationship.

Allowed:

- direct upcast
- direct subtype-compatible projection expressible as `fun env -> env :> NeededEnv`
- generated environment interface already guarantees the required smaller environment

Not allowed:

- inventing field-based projections
- generating ad hoc record reconstruction
- guessing how one application environment should be converted into another

If the needed environment cannot be derived mechanically, the generated wrapper must not attempt adaptation. That mismatch must remain visible to the caller.

---

## Parameter Rules

Generated wrapper parameters should mirror the source member signature.

Examples:

```fsharp
abstract debug: string -> unit
abstract tryFind: int * string -> Result<User, LookupError>
abstract create: id: string * count: int -> Task<Result<Order, OrderError>>
```

Generated wrappers should preserve:

- argument count
- argument order
- tuple structure where relevant
- curried structure where relevant

The generator should not collapse or reorder arguments.

---

## Error Channel Rules

The generated wrapper must preserve the original member’s error information as far as the return shape allows.

Examples:

- plain return -> wrapper error type remains unconstrained, usually `'e`
- `Result<'t, 'e>` -> wrapper error type is `'e`
- `Task<Result<'t, 'e>>` -> wrapper error type is `'e`
- `Eff<'t, 'e, _>` -> wrapper error type is `'e`

Thrown exceptions from the member invocation itself are handled by the normal `Eff.read` / `Eff.ofTask` / `Eff.ofAsync` / `Eff.ofValueTask` semantics of the runtime.

---

## Accessibility Rules

Generated environment interfaces and modules are public unless the source generation context explicitly requires narrower visibility.

The generator must not expose:

- `EffRuntime`
- `EffNodes`
- internal `Node` types
- boxed runtime machinery

Generated code should depend only on the public API in `EffFs.Core`.

---

## Example

Input:

```fsharp
[<Effect>]
type ILogger =
  abstract debug: string -> unit
  abstract error: string -> unit
  abstract flush: unit -> Task
  abstract tryWrite: string -> Result<unit, LogError>
  abstract spawnWrite: string -> Eff<Fiber<unit>, LogError, ILoggerEnv>
```

Generated shape:

```fsharp
[<Interface>]
type ELogger =
  abstract Logger: ILogger

module ELogger =
  let debug (message: string) : Eff<unit, 'e, #ELogger> =
    Eff.read (fun (env: #ELogger) -> env.Logger.debug message)

  let error (message: string) : Eff<unit, 'e, #ELogger> =
    Eff.read (fun (env: #ELogger) -> env.Logger.error message)

  let flush () : Eff<unit, 'e, #ELogger> =
    Eff.read (fun (env: #ELogger) -> env.Logger.flush())
    |> Eff.bind (fun t -> Eff.ofTask (fun () -> t))

  let tryWrite (message: string) : Eff<unit, LogError, #ELogger> =
    Eff.read (fun (env: #ELogger) -> env.Logger.tryWrite message)
    |> Eff.bind Eff.ofResult

  let spawnWrite (message: string) : Eff<Fiber<unit>, LogError, #ELogger> =
    Eff.read (fun (env: #ELogger) ->
      env.Logger.spawnWrite message
      |> Eff.provideFrom (fun (outer: #ELogger) -> outer :> ILoggerEnv))
    |> Eff.flatten
```

---

## Non-Goals

This feature does not:

- generate arbitrary environment projections
- inspect or depend on `Eff` runtime internals
- change the runtime behavior of `Eff`
- replace normal user-authored `Eff` functions

It only generates wrappers that normalize interface members into the existing `Eff` API.
