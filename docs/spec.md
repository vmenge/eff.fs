# F# IO Effect Spec

## Goals

Build an `IO` effect for F# with these priorities:

- good external developer ergonomics
- strong performance
- support for sync and async effects
- support for environment-based dependency injection
- lazy effect construction
- unified error handling
- small composable environments

Internal implementation ergonomics are less important than runtime behavior and external API quality.

---

## Core Representation

Primary shape:

```fsharp
[<Struct>]
type IO<'T, 'Env> =
  | Pure of 'T
  | Error of exn
  | Pending of IONode<'T, 'Env>
```

Notes:

- `IO` is a struct discriminated union.
- `Pure` is the zero-cost success fast path.
- `Error` stores `exn` directly.
- `Pending` stores a private internal runtime node.
- No extra public branch for “early exit”.

### Why `exn`

Chosen over a custom internal error union because:

- simpler runtime
- less branching
- better interop with .NET exceptions, `Task`, `Async`
- preserves stack traces when the original exception is kept
- lower implementation complexity

`Result` and `Option` inputs are normalized into this error channel.

---

## Error Handling

### Internal model

All failures normalize to:

```fsharp
Error of exn
```

Examples:

- thrown exception -> `Error ex`
- faulted `Task` -> `Error ex`
- faulted `Async` -> `Error ex`
- `Result.Error e` -> mapped to an exception
- `None` -> mapped to an exception

### Stack traces

Using `exn` preserves stack traces as long as the original exception object is kept.

If an error source is not originally an exception, such as `Option.None` or `Result.Error`, any generated exception only has a stack trace from the point where it was created.

### CE exception behavior

The `io {}` computation expression should automatically catch exceptions thrown inside user callbacks and normalize them into `Error exn`.

This applies to:

- pure synchronous user code inside the CE
- `bind`
- `map`
- `catch`
- conversions from `Task`, `Async`, Promise
- cleanup/finalizer code as appropriate

Exceptions should not leak through the effect abstraction by default.

---

## Laziness

Effects should be lazy by default.

Meaning:

- constructing an `IO` value should not start work
- running happens only via `run*` functions
- interop constructors from eager worlds should take thunks

Preferred constructor shapes:

```fsharp
IO.sync      : (unit -> 'T) -> IO<'T, 'Env>
IO.delay     : (unit -> IO<'T, 'Env>) -> IO<'T, 'Env>
IO.fromTask  : (unit -> Task<'T>) -> IO<'T, 'Env>
IO.fromAsync : (unit -> Async<'T>) -> IO<'T, 'Env>
```

Benefits:

- referential transparency
- better retry/finalizer semantics
- better composability
- easier guard/ensure-style APIs
- clearer control over when effects begin

Performance note:

- laziness is not automatically faster
- main cost comes from closures/thunks and extra nodes
- `Pure` and `Error` should remain strict and cheap

---

## Pending Representation

`Pending` holds a private `IONode<'T, 'Env>` directly.

No extra `IOPending` wrapper is required.

```fsharp
[<Struct>]
type IO<'T, 'Env> =
  | Pure of 'T
  | Error of exn
  | Pending of IONode<'T, 'Env>
```

### `IONode`

`IONode<'T, 'Env>` is private and stable.

Conceptual shape:

```fsharp
type IONode<'T, 'Env> =
    abstract Step : 'Env -> ValueTask<IO<'T, 'Env>>
```

This is the preferred mental model:

- `Pure` and `Error` are terminal
- `Pending node` means the runtime should ask the node for the next step
- the node may represent delayed sync work, task/async interop, bind chains, etc.

### Why `IONode` is a reference type

Preferred because pending computations typically require:

- stable identity
- mutable shared runtime state
- continuation storage
- recursive/composed structure
- async interop with heap-backed mechanisms

A struct `IONode` is possible in theory, but not preferred as the core suspended runtime state.

---

## Why not `Pending of ValueTask<IO<'T, 'Env>>`

Rejected as the primary public representation.

Example rejected shape:

```fsharp
[<Struct>]
type IO<'T, 'Env> =
  | Pure of 'T
  | Error of exn
  | Pending of ValueTask<IO<'T, 'Env>>
```

Reasons:

- `ValueTask` has one-shot/sensitive consumption semantics
- storing it directly in a copyable struct union is fragile
- copying `IO` copies the `ValueTask`
- makes the public `IO` type fatter than desired
- couples public representation too tightly to a low-level async primitive

`ValueTask` may still be used internally inside `IONode`.

---

## Running IO

### Public runners

On .NET:

```fsharp
IO.runSync  : 'Env -> IO<'T, 'Env> -> Result<'T, exn>
IO.runTask  : 'Env -> IO<'T, 'Env> -> Task<Result<'T, exn>>
IO.runAsync : 'Env -> IO<'T, 'Env> -> Async<Result<'T, exn>>
```

On Fable:

```fsharp
IO.runPromise : 'Env -> IO<'T, 'Env> -> JS.Promise<Result<'T, exn>>
```

### `runSync`

`runSync` is valid and useful.

It is especially appropriate at top-level boundaries such as:

- process entrypoints
- CLI tools
- test runners
- worker entrypoints
- outermost host boundaries under your control

It should not be the only runner, because:

- request-level/server-internal code often should stay async
- UI code should not block
- JS/Fable cannot provide the same normal blocking semantics

---

## Environment / Dependency Injection

Environment support is desired and should be composable.

Chosen shape:

```fsharp
type IO<'T, 'Env>
```

### Why `'T` first and `'Env` second

This reads better in the common case and allows nicer env-less aliases.

Inference usually leaves `'Env` generic if unconstrained.

Example:

```fsharp
IO.pure 123
```

should infer something equivalent to:

```fsharp
IO<int, 'Env>
```

not force `unit`.

### No default generic parameter

F# does not provide the desired default generic argument behavior here, so aliases should be used where helpful.

Possible alias pattern:

```fsharp
type Eff<'T, 'Env> = IO<'T, 'Env>
type IO0<'T> = IO<'T, unit>
```

Naming remains open.

---

## Dependency Injection Style

Large monolithic env records were rejected as the primary model because ergonomics become poor.

Two candidate DI styles were discussed.

### Preferred style: capability interfaces

Define small capabilities:

```fsharp
type IHasLogger =
    abstract Logger : ILogger

type IHasDb =
    abstract Db : IDb
```

Then functions depend only on what they need:

```fsharp
val logInfo  : string -> IO<unit, 'Env> when 'Env :> IHasLogger
val getUser  : int -> IO<User, 'Env> when 'Env :> IHasDb
val greetUser : int -> IO<string, 'Env> when 'Env :> IHasLogger and 'Env :> IHasDb
```

Concrete env values implement multiple interfaces.

This gives:

- small env requirements
- decent composition
- no giant record in every function signature

### Secondary style: small records + adapters

Also possible:

```fsharp
type LoggerEnv = { Logger : ILogger }
type DbEnv = { Db : IDb }
```

Then use adapter functions or `local` to run smaller-env effects inside bigger-env effects.

This is more explicit but more boilerplate-heavy.

### Chosen preference

Capability interfaces are preferred over small-record adapters for primary ergonomics.

---

## Computation Expression Semantics

The CE should support `let!` over multiple source types by converting them into `IO`.

Supported conceptual sources:

- `IO<'T, 'Env>`
- `Result<'T, 'E>`
- `Option<'T>`
- `Task<'T>`
- `Async<'T>`
- Promise under Fable

This can be implemented with `Source` overloads or overloaded `Bind`, with a preference for normalizing sources into `IO` first.

Example target usage:

```fsharp
io {
  let! x = someIo
  let! y = someResult
  let! z = someOption
  let! t = someTask
  return x
}
```

### Source normalization

Preferred strategy:

- `Result` -> immediate `Pure` / `Error`
- `Option` -> immediate `Pure` / `Error`
- `Task` / `Async` / Promise -> lazy normalized pending node
- no storing of `Result` or `Option` inside `Pending`

---

## Guard / Ensure / Early Return

No extra public union case should be added for early exit.

Rejected idea:

- adding a dedicated `Exit` branch to `IO`

Preferred direction:

- use helper combinators that preserve the current value or short-circuit through existing semantics

The most practical API shape is value-preserving helpers such as:

```fsharp
IO.ensure : ('T -> bool) -> IO<'T, 'Env> -> IO<'T, 'Env>
```

Example:

```fsharp
loadUser id
|> IO.ensure isActive
```

Inside CE:

```fsharp
io {
  let! user = loadUser id |> IO.ensure isActive
  return user
}
```

This preserves the successful value and stops the computation if the predicate fails.

The exact internal encoding of this short-circuit remains open, but no extra public DU branch should be added.

---

## Cleanup / Defer

Defer-style cleanup is desired, similar in spirit to Go/Odin `defer`.

Desired capabilities:

```fsharp
IO.defer      : (unit -> unit) -> IO<unit, 'Env>
IO.deferIO    : (unit -> IO<unit, 'Env>) -> IO<unit, 'Env>
IO.finallyDo  : (unit -> unit) -> IO<'T, 'Env> -> IO<'T, 'Env>
IO.finallyIO  : (unit -> IO<unit, 'Env>) -> IO<'T, 'Env> -> IO<'T, 'Env>
IO.bracket    : IO<'R, 'Env> -> ('R -> IO<unit, 'Env>) -> ('R -> IO<'T, 'Env>) -> IO<'T, 'Env>
```

Semantics:

- cleanup runs on success
- cleanup runs on failure
- multiple defers run in LIFO order
- cleanup should also run when computations short-circuit

---

## Public API Direction

### Core constructors

```fsharp
IO.pure      : 'T -> IO<'T, 'Env>
IO.error     : exn -> IO<'T, 'Env>
IO.sync      : (unit -> 'T) -> IO<'T, 'Env>
IO.delay     : (unit -> IO<'T, 'Env>) -> IO<'T, 'Env>
IO.fromTask  : (unit -> Task<'T>) -> IO<'T, 'Env>
IO.fromAsync : (unit -> Async<'T>) -> IO<'T, 'Env>
```

### Core combinators

```fsharp
IO.map       : ('T -> 'U) -> IO<'T, 'Env> -> IO<'U, 'Env>
IO.bind      : ('T -> IO<'U, 'Env>) -> IO<'T, 'Env> -> IO<'U, 'Env>
IO.catch     : (exn -> IO<'T, 'Env>) -> IO<'T, 'Env> -> IO<'T, 'Env>
IO.mapError  : (exn -> exn) -> IO<'T, 'Env> -> IO<'T, 'Env>
IO.ensure    : ('T -> bool) -> IO<'T, 'Env> -> IO<'T, 'Env>
```

### Environment helpers

Exact names remain open, but conceptually:

```fsharp
IO.ask       : IO<'Env, 'Env>
IO.service   : IO<'Service, 'Env> when 'Env :> IHasServiceLike
IO.provide   : 'Env -> IO<'T, 'Env> -> IO<'T, unit>   // or direct runner-oriented variant
IO.local     : ('OuterEnv -> 'InnerEnv) -> IO<'T, 'InnerEnv> -> IO<'T, 'OuterEnv>
```

---

## Performance Priorities

Primary performance principles:

- `Pure` path must be as cheap as possible
- `Error` path must stay simple
- pending path should allocate only when genuinely suspended/effectful
- avoid large internal error unions
- prefer normalization of source types early
- do not expose `ValueTask` directly as the public pending representation
- avoid unnecessary closures/thunks in hot paths
- laziness should exist at the effect boundary, not as pointless overhead everywhere

### Main expected costs

The likely hotspots are:

- closure allocation
- node allocation for pending work
- task/async/promise interop
- async state machine churn
- virtual dispatch in runtime nodes
- large/copy-heavy value types

Branching on `Pure | Error | Pending` is acceptable and part of the chosen design.

---

## Open Questions

These were left intentionally open or only partially decided:

1. Exact internal encoding of `IONode`
   - interface with implementations
   - sealed node type with tags
   - other optimized runtime representation

2. Exact internal encoding of short-circuit behavior for `ensure`/guard-like helpers
   - public API direction is chosen
   - internal control mechanism remains open

3. Final public naming
   - `IO<'T, 'Env>` vs a two-type naming split such as `EnvIO<'T, 'Env>` + alias

4. Exact promise interop API on Fable

5. Whether to expose both `runTask` and `runValueTask`
   - `runTask` is clearly desired
   - `runValueTask` remains optional/advanced

---

## Summary of Chosen Decisions

Chosen:

- `IO<'T, 'Env>`
- struct DU
- `Pure | Error of exn | Pending of IONode`
- errors represented as `exn`
- effects lazy by default
- `Task`/`Async`/Promise constructors should take thunks
- no extra public `Exit` branch
- no need for `IOPending` wrapper
- `IONode` private and reference-based
- DI via small capability interfaces is preferred
- multiple source types can participate in `let!` by normalization into `IO`
- `runSync` is valid at top-level boundaries
- defer/finally/bracket-style cleanup should exist

Rejected:

- large internal error union as the primary model
- giant monolithic env as the only DI story
- public `Pending of ValueTask<IO<...>>`
- extra public DU branch just for early exit
