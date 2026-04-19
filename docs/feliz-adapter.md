# Feliz Adapter Proposal

## Goal

Add an `EffSharp.Feliz` adapter focused on manufacturing React/Feliz hooks from
ordinary `Eff` programs.

The design should:

- keep `'env` bubbling through `Eff` all the way to the edge
- avoid React context as the primary environment mechanism
- avoid introducing a special root runner when existing `Eff.runTask` is
  sufficient
- let users resolve environment-dependent hook factories once, then use the
  resulting hooks inside normal React components
- preserve React's hook rules by making hook composition order explicit and
  stable
- keep the public surface small and production-oriented

This proposal adds:

- a hooks-first package `EffSharp.Feliz`
- a helper module `Hook`
- default hook-factory constructors `Hook.mkUse` and `Hook.mkUseEffect`
- minimal structural combinators `Hook.map` and `Hook.zip`

This proposal does not add:

- a special `createRootEff`
- an adapter-owned root API
- a built-in async UI state algebra

---

## Core Model

The adapter is centered on the idea that a hook factory is produced by an
ordinary effect:

```fsharp
Eff<'args -> 'a, 'e, 'env>
```

This means:

- `'env` is resolved through EffSharp
- `'args` are still supplied by the React component at hook call time
- the final result is a plain hook function used inside a normal component

### Why `'args` Is Generic

`'args` must remain generic rather than being fixed to `unit`.

That allows hooks whose runtime inputs still come from props or component-local
state:

```fsharp
let useUser =
  Hook.mkUse (fun userId ->
    Api.getUser userId)
```

Type:

```fsharp
Eff<string -> User, ApiError, #Effect.Api>
```

After resolving the effect, the component receives a normal hook:

```fsharp
useUser : string -> User
```

If `'args` were fixed to `unit`, the adapter could not express ordinary
parameterized hooks such as `useUser userId` or `useRoom roomId`.

---

## Root Boundary

The adapter does not need a special root API.

The host application can use the normal EffSharp runner:

```fsharp
let! exit = App.app () |> Eff.runTask env
```

and then decide how to mount with normal Feliz/React APIs.

This keeps the adapter surface smaller and avoids inventing a second root
resolution concept on top of `Eff.runTask`.

---

## Public Package Shape

Package name:

```text
EffSharp.Feliz
```

Helper module:

```fsharp
module Hook
```

Supporting alias:

```fsharp
type DependencyList = obj array
```

`DependencyList` is an alias over `obj array` because React dependency lists are
heterogeneous.

---

## Public API

The package should publish the following baseline surface:

```fsharp
module Hook =
  val map :
    ('a -> 'b) ->
    Eff<'args -> 'a, 'e, 'env> ->
      Eff<'args -> 'b, 'e, 'env>

  val zip :
    Eff<'args -> 'a, 'e, 'env> ->
    Eff<'args -> 'b, 'e, 'env> ->
      Eff<'args -> 'a * 'b, 'e, 'env>

  val mkUse :
    ('args -> Eff<'a, 'e, 'env>) ->
      Eff<'args -> 'a, 'e, 'env>

  val mkUseEffect :
    ('args -> Eff<unit, 'e, 'env>) ->
      Eff<'args -> DependencyList -> unit, 'e, 'env>

  val mkUseEffect :
    ('args -> Eff<unit -> unit, 'e, 'env>) ->
      Eff<'args -> DependencyList -> unit, 'e, 'env>
```

### Meaning

`Hook.map` and `Hook.zip` are structural combinators over effect-produced hook
factories.

`Hook.mkUse` lifts an ordinary effectful computation into a hook factory.

`Hook.mkUseEffect` lifts an ordinary effectful setup computation into a hook
factory for `useEffect`, with the dependency list supplied at hook call time.
It supports both a no-cleanup overload and a cleanup-returning overload.

---

## Helper Contract

The helper layer must obey the following formal rules:

- helper-produced hooks must invoke their underlying hooks in a fixed order
- helper-produced hooks must not branch on hook invocation
- helper combinators only compose hook factories with the same `'args`
- `unit` is the natural zero-argument specialization

This is why the public helper layer is intentionally small.

The adapter should not provide monadic or dynamically branching hook
combinators, because those make it too easy to violate React's hook-ordering
requirements.

---

## `Hook.mkUse`

`Hook.mkUse` is a thin constructor.

It exists so authors can write ordinary EffSharp code:

```fsharp
let useUser =
  Hook.mkUse (fun userId ->
    eff {
      return! Api.getUser userId
    })
```

and obtain an effect that resolves to a real hook:

```fsharp
Eff<string -> User, ApiError, #Effect.Api>
```

After environment resolution, the resulting hook is used inside a normal React
component:

```fsharp
[<ReactComponent>]
let UserPage(useUser: string -> User, userId: string) =
  let user = useUser userId
  Html.text user.Name
```

`Hook.mkUse` is thin by design. It should not impose a built-in async state
model or broader UI framework policy.

---

## `Hook.mkUseEffect`

`Hook.mkUseEffect` is also a thin constructor.

It exists so authors can write ordinary effectful setup logic:

```fsharp
let useRoomSubscription =
  Hook.mkUseEffect (fun roomId ->
    eff {
      do! Chat.subscribe roomId
    })
```

and obtain an effect that resolves to a real hook:

```fsharp
Eff<string -> DependencyList -> unit, ChatError, #Effect.Chat>
```

After environment resolution, the resulting hook is used inside a normal React
component:

```fsharp
[<ReactComponent>]
let RoomPage(useRoomSubscription: string -> DependencyList -> unit, roomId: string) =
  useRoomSubscription roomId [| box roomId |]
  Html.text $"Room: {roomId}"
```

The dependency list is explicit because dependency comparison is part of React's
runtime semantics, not something the adapter should hide or infer.

When cleanup is needed, the cleanup-returning overload is used:

```fsharp
let useRoomConnection =
  Hook.mkUseEffect (fun roomId ->
    eff {
      let! disconnect = Chat.connect roomId
      return disconnect
    })
```

Type:

```fsharp
Eff<string -> DependencyList -> unit, ChatError, #Effect.Chat>
```

---

## Worked Composition Example

The minimal helper layer should be enough to combine resolved hooks without
inventing a larger framework:

```fsharp
let useTheme =
  Hook.mkUse (fun () -> Theme.current ())

let useUser =
  Hook.mkUse (fun userId -> Api.getUser userId)

let useThemeAndUser =
  Hook.zip useTheme useUser
```

Type:

```fsharp
Eff<'args -> Theme * User, 'e, 'env>
```

where both hooks share the same `'args` shape.

---

## Effectful Views

Effectful view builders remain valid as a pattern:

```fsharp
Eff<ReactElement, 'e, 'env>
```

They are not the center of the adapter package.

The package is hooks-first. Effectful views should be documented as a pattern on
top of ordinary EffSharp composition, not as the primary public API.

---

## State Model

The adapter must remain agnostic about async UI state representation.

It should not standardize a built-in `AsyncState<'t, 'e>` or similar type.
Different applications may want:

- suspense-first usage
- explicit loading/error unions
- cached resource models
- domain-specific state machines

That policy belongs to the consuming application, not to `EffSharp.Feliz`.

---

## React Semantics

The adapter must preserve these React rules:

- the final produced hooks are normal hooks and must be called from components
  or custom hooks
- `Hook.mkUse` and `Hook.mkUseEffect` are constructors, not escape hatches for
  violating React's lifecycle rules
- the adapter must not encourage calling component functions directly
- the adapter must not hide dependency-list semantics for `useEffect`

---

## Prerequisite

This package assumes that `Eff` itself is consumable from Fable.

That prerequisite should be stated explicitly, but the adapter spec does not
attempt to redesign the core/runtime structure in this document.

---

## Recommendation

Build `EffSharp.Feliz` as a small hooks-first adapter:

- no special root API
- no context-based env escape hatch
- no built-in async state algebra
- plain hook factories produced from ordinary EffSharp programs
- minimal structural helpers only

This keeps the adapter honest, small, and compatible with the design goal that
`'env` should remain an EffSharp concern rather than being erased into React
infrastructure.
