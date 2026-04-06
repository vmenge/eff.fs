# F# Eff Current Spec

This document describes the implementation that exists today in `pkgs/Core/src`.

If this file disagrees with the code, the code wins. This spec is meant to be a map of the current system, not a design wish list.

## Goals

The current library is a small, lazy effect system for F# with these priorities:

- explicit success, managed-error, and defect channels
- first-class environment access
- lazy interop with synchronous and asynchronous work
- stack-safe composition
- deterministic cleanup
- ergonomic use through pipelines and computation expressions

## Core Types

### `Eff<'t, 'e, 'env>`

The public effect type is:

```fsharp
[<Struct; RequireQualifiedAccess>]
type Eff<'t, 'e, 'env> =
  internal
  | Pure of value: 't
  | Err of err: 'e
  | Crash of exn: exn
  | Suspend of suspend: (unit -> Eff<'t, 'e, 'env>)
  | Thunk of thunk: (unit -> 't)
  | Task of tsk: (unit -> Task<'t>)
  | Read of read: ('env -> 't)
  | Node of Node<'t, 'e, 'env>
```

Meaning of each case:

- `Pure` is an already-available successful value.
- `Err` is a managed error value of type `'e`.
- `Crash` is an unrecovered exception. This is the defect channel.
- `Suspend` delays production of another `Eff`.
- `Thunk` delays synchronous work that returns a value.
- `Task` delays asynchronous work backed by `Task<'t>`.
- `Read` reads from the current environment.
- `Node` is the internal representation for composed operations such as `bind`, `catch`, `defer`, and `provideFrom`.

The union cases are internal. User code constructs effects through the `Eff` module and the computation expressions.

### `Exit<'t, 'e>`

Runners return:

```fsharp
[<RequireQualifiedAccess>]
type Exit<'t, 'e> =
  | Ok of 't
  | Err of 'e
  | Exn of exn
```

Meaning:

- `Ok` is successful completion.
- `Err` is completion with a managed error.
- `Exn` is completion with an unrecovered defect.

The helper module `Exit` exposes `isOk`, `ok`, `err`, and `ex`.

### `Report`

`Report` is an exception wrapper used when the library needs an `exn` surface without discarding the original payload.

```fsharp
type Report(o: obj, msg: string, ?inner: exn) =
  inherit System.Exception(msg, defaultArg inner null)
  member _.Err = o
```

`Report.make` and `Report.makewith` preserve existing `Report` instances, wrap plain exceptions once, and otherwise store arbitrary values in `Err`.

The active pattern `ReportAs` extracts the original payload from a `Report`.

## Error Model

The current runtime is tri-channel:

- success: `'t`
- managed error: `'e`
- defect: `exn`

This distinction is central to the library.

Managed errors:

- are created with `Eff.Err`
- flow through `mapErr`, `orElse`, `orElseWith`, and `tapErr`
- are returned by runners as `Exit.Err`

Defects:

- are created with `Eff.Crash`
- also arise when user code throws inside `Thunk`, `Suspend`, `Task`, `Read`, mapping functions, bind continuations, cleanup code, and other runtime callbacks
- are handled with `catch` and `tapExn`
- are returned by runners as `Exit.Exn`

This means exceptions are not automatically turned into managed errors. If the user wants thrown exceptions to become managed errors, they must opt in with `tryCatch`, `tryTask`, or `tryAsync`.

### Capturing Exceptions as Managed Errors

The library exposes:

```fsharp
Eff.tryCatch : (unit -> 't) -> Eff<'t, exn, 'env>
Eff.tryTask  : (unit -> Task<'t>) -> Eff<'t, exn, 'env>
Eff.tryAsync : (unit -> Async<'t>) -> Eff<'t, exn, 'env>
```

These helpers catch thrown exceptions and return them through the managed error channel as `exn`.

## Laziness

Effects are lazy at effect boundaries:

- `Pure`, `Err`, and `Crash` already contain values
- `Suspend`, `Thunk`, `Task`, and `Read` do not run until interpreted by a runner
- `ofTask`, `ofValueTask`, and `ofAsync` all take thunks so work starts only when the effect runs

This is an execution model, not a promise of zero allocation. Composed programs still build runtime nodes and closures.

## Environment Model

The library has built-in reader-style environment access:

```fsharp
Eff.ask  : unit -> Eff<'env, 'e, 'env>
Eff.read : ('env -> 't) -> Eff<'t, 'e, 'env>
```

There are two ways to shape environments:

- concrete records or anonymous records
- small capability interfaces used through subtype constraints

Both styles work today. The tests and example use both.

Environment substitution is explicit:

```fsharp
Eff.provideFrom : ('outer -> 'inner) -> Eff<'t, 'e, 'inner> -> Eff<'t, 'e, 'outer>
Eff.provide     : 'env -> Eff<'t, 'e, 'env> -> Eff<'t, 'e, unit>
```

`provideFrom` scopes only the subtree it wraps. The outer environment remains visible before and after that subtree. Projected environments survive suspension points because `provideFrom` runs the inner effect in a child runtime and resumes the outer one when the inner machine completes.

## Constructors and Conversions

The public construction surface in `Eff` is:

```fsharp
Eff.Pure      : 't -> Eff<'t, 'e, 'env>
Eff.Err       : 'e -> Eff<'t, 'e, 'env>
Eff.Crash     : exn -> Eff<'t, 'e, 'env>
Eff.Suspend   : (unit -> Eff<'t, 'e, 'env>) -> Eff<'t, 'e, 'env>
Eff.Thunk     : (unit -> 't) -> Eff<'t, 'e, 'env>
Eff.Task      : (unit -> Task<'t>) -> Eff<'t, 'e, 'env>
Eff.Read      : ('env -> 't) -> Eff<'t, 'e, 'env>
```

Lowercase aliases and helpers also exist:

```fsharp
Eff.suspend
Eff.thunk
Eff.failw
Eff.report
Eff.reportw
```

Interop helpers:

```fsharp
Eff.ofResult
Eff.ofResultWith
Eff.ofOption
Eff.ofOptionWith
Eff.ofValueOption
Eff.ofValueOptionWith
Eff.ofTask
Eff.ofValueTask
Eff.ofAsync
```

Current conversion rules:

- `Result.Ok` becomes `Pure`
- `Result.Error` becomes `Err`
- `Option.None` becomes `Err (Report.make None)`
- `ValueOption.ValueNone` becomes `Err (Report.make ValueNone)`
- `ValueTask` is normalized into the `Task` path
- `Async` is normalized into the `Task` path with `Async.StartAsTask`

## Core Combinators

The current public combinators are:

```fsharp
Eff.map
Eff.bind
Eff.mapErr
Eff.flatten
Eff.filterOr
Eff.orElse
Eff.orElseWith
Eff.tap
Eff.tapErr
Eff.catch
Eff.tapExn
Eff.defer
Eff.bracket
Eff.orRaise
Eff.orRaiseWith
```

Key semantics:

- `map` transforms only successful values.
- `bind` sequences successful values.
- `mapErr` transforms only managed errors.
- `orElse` and `orElseWith` recover only managed errors.
- `catch` recovers only defects.
- `tap` preserves the original success value.
- `tapErr` preserves the original managed error unless the tap fails.
- `tapExn` preserves the original defect unless the tap fails.
- `flatten` runs nested effects in the same ambient environment.
- `filterOr` preserves the successful value when the predicate passes and switches to `orFn` when it fails.
- `orRaise` and `orRaiseWith` convert managed errors into defects.

## Cleanup Semantics

The library supports cleanup in two ways:

```fsharp
Eff.defer   : Eff<unit, 'e, 'env> -> Eff<'t, 'e, 'env> -> Eff<'t, 'e, 'env>
Eff.bracket : Eff<'r, 'e, 'env> -> ('r -> Eff<unit, 'e, 'env>) -> ('r -> Eff<'t, 'e, 'env>) -> Eff<'t, 'e, 'env>
```

Current guarantees:

- cleanup runs on success
- cleanup runs on managed error
- cleanup runs on defect
- multiple defers run in LIFO order
- `bracket` release runs even when `usefn` throws before returning an effect

Override rules:

- if cleanup returns `Err`, that managed error replaces the earlier result
- if cleanup crashes, that defect replaces the earlier result
- for `bracket`, release failure overrides both body success and body managed error

These semantics are deliberate. Cleanup failure wins.

## Computation Expressions

### `eff`

The default builder is `eff`.

It supports:

- `return`
- `return!`
- `let!`
- `do!`
- `use`
- `use!`
- `while`
- `for`
- `defer` as a custom operation

Supported `let!` sources are:

- `Eff<'t, 'e, 'env>`
- `Result<'t, 'e>`
- `Option<'t>`
- `ValueOption<'t>`
- `Task<'t>`
- `ValueTask<'t>`
- `Async<'t>`
- `Task<Result<'t, 'e>>`
- `ValueTask<Result<'t, 'e>>`
- `Async<Result<'t, 'e>>`

Important behavior:

- `Result.Error` short-circuits through the managed error channel
- `Option.None` and `ValueOption.ValueNone` short-circuit as `Report`-backed `exn` errors
- exceptions thrown directly inside CE user code are defects, not managed errors
- `use` and `use!` are implemented through `bracket`
- `for` keeps the enumerator alive across effectful bodies and disposes it at the end

### `effr`

The second builder is `effr`.

`effr` keeps the same control-flow behavior as `eff` but normalizes managed errors to `exn` by mapping them through `Report.make`.

This gives an `Eff<'t, exn, 'env>` surface while preserving original error payloads inside `Report`.

Current normalization rules:

- existing `Report` values flow through unchanged
- plain exceptions become `Report` with the original exception as `InnerException`
- non-exception payloads become `Report` carrying the original payload

`effr` does not collapse defects into managed errors. Defects remain defects.

## Running Effects

The current runners are:

```fsharp
Eff.runTask : 'env -> Eff<'t, 'e, 'env> -> Task<Exit<'t, 'e>>
Eff.runSync : 'env -> Eff<'t, 'e, 'env> -> Exit<'t, 'e>
```

`runSync` is implemented by awaiting `runTask`.

There is no public `runAsync`, `runValueTask`, or Fable `runPromise` runner in the current codebase.

## Runtime Architecture

The runtime is not based on a public `Pending` case. It is based on:

- specialized leaf cases on `Eff`
- internal `Node` subclasses for composed work
- a frame-based interpreter
- a small machine/trampoline for async suspension

Composition nodes live in `EffNodes`:

- `Map`
- `FlatMap`
- `MapErr`
- `FlatMapErr`
- `FlatMapExn`
- `Defer`
- `Bracket`
- `ProvideFrom`

Interpretation lives in `EffRuntime`:

- `stepEff` executes one effect step
- `unwind` walks frames after success, managed error, or defect
- `TypedMachine` and `runTaskLoop` drive asynchronous progress
- `RuntimeStepper` carries the environment and supports projection into child environments

The runtime uses boxed values internally when crossing generic boundaries. This is part of the current design.

## Tested Guarantees

The test suite currently asserts all of the following:

- deep `Suspend` chains are stack-safe
- deep `bind` chains are stack-safe
- `Task` and `Async` faults preserve the original exception rather than wrapping it in `AggregateException`
- canceled tasks surface as `TaskCanceledException`
- environment projection survives asynchronous suspension
- cleanup runs before outer `catch` observes a defect
- `for` loops dispose enumerators even when the body fails

These behaviors are part of the current contract.

## Current Non-Goals

This repository does not currently implement:

- an `Eff<'t, 'env>` exn-only core type
- a public `Pending of obj * Step` representation
- automatic conversion of all exceptions into managed errors
- `ensure`, `finallyDo`, or `finallyIO` helpers
- parallel composition or concurrency combinators
- alternate runtimes for Fable or promises

## Minimal Example

```fsharp
open EffSharp.Core

type ILogger =
  abstract Debug: string -> unit

type HasLogger =
  abstract Logger: ILogger

let log msg =
  Eff.read (fun (env: #HasLogger) -> env.Logger.Debug msg)

let program =
  eff {
    do! log "starting"
    let! value = Ok 41
    return value + 1
  }
```

This example uses the current design:

- typed managed errors
- explicit environment reads
- lazy execution until a runner interprets the effect
