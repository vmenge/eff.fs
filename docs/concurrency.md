# Concurrency Proposal

## Goal

Add strict structured concurrency to EffSharp on top of .NET `Task`.

The design should:

- keep the current interpreter and cleanup semantics
- make concurrency tree-shaped, not runner-global
- treat manual cancellation as a distinct runtime outcome
- keep timeout separate from cancellation
- avoid historical-fiber retention so the runtime can handle very large fiber counts

This proposal adds:

- two fork primitives: `fork` and `forkOn`
- a fiber handle type
- derived combinators: `race`, `all`, `timeout`
- a 4th exit channel: `Aborted`

Cancellation remains internal to the runtime. User code does not receive or pass `CancellationToken`.

---

## API Surface

### Types

```fsharp
type Fiber<'t, 'e>

type Exit<'t, 'e> =
  | Ok of 't
  | Err of 'e
  | Exn of exn
  | Aborted

type TimeoutResult<'t> =
  | Completed of 't
  | TimedOut
```

### Fiber Operations

```fsharp
module Fiber =
  /// Observe the fiber's outcome without re-raising.
  /// Always succeeds with an Exit value.
  val await : Fiber<'t, 'e> -> Eff<Exit<'t, 'e>, never, 'env>

  /// Join the fiber and adopt its outcome into the caller.
  /// Ok / Err / Exn / Aborted all propagate through the caller's channels.
  val join  : Fiber<'t, 'e> -> Eff<'t, 'e, 'env>

  /// Signal the fiber to stop. Waits for cleanup (defer/bracket)
  /// to complete before returning. If the fiber is already complete,
  /// abort is a silent no-op.
  val abort : Fiber<'t, 'e> -> Eff<unit, 'e2, 'env>
```

### Fork Primitives

```fsharp
module Eff =
  /// Start an effect concurrently. The child begins on the caller's
  /// thread and yields at the first async boundary. Returns a fiber
  /// handle. Best for I/O-bound work.
  val fork   : Eff<'t, 'e, 'env> -> Eff<Fiber<'t, 'e>, 'e2, 'env>

  /// Start an effect on the ThreadPool via Task.Run. The child
  /// immediately moves to a ThreadPool thread. Returns a fiber
  /// handle. Best for CPU-bound or blocking work.
  val forkOn : Eff<'t, 'e, 'env> -> Eff<Fiber<'t, 'e>, 'e2, 'env>
```

### Derived Combinators

```fsharp
module Eff =
  /// Run two effects concurrently. The first to complete wins.
  /// The loser is aborted and awaited for cleanup.
  val race : Eff<'t, 'e, 'env> -> Eff<'t, 'e, 'env> -> Eff<'t, 'e, 'env>

  /// Run all effects concurrently. If any child finishes Err / Exn / Aborted,
  /// the rest are aborted.
  /// Returns all results in order on success.
  val all : Eff<'t, 'e, 'env> list -> Eff<'t list, 'e, 'env>

  /// Run an effect with a time limit. Timeout is distinct from manual abort.
  /// If the timer wins, the child is aborted. If cleanup succeeds the result
  /// is TimedOut. If cleanup fails, that Err / Exn overrides TimedOut.
  val timeout : TimeSpan -> Eff<'t, 'e, 'env> -> Eff<TimeoutResult<'t>, 'e, 'env>
```

---

## Ownership Model

Structured concurrency in EffSharp is parent-owned.

Each fiber owns:

- its own abort token source
- its own final `Exit`
- its own live direct-child set
- a reference to its parent, if any

The root `runTask` fiber owns the root scope.

When a fiber forks a child:

1. create child handle
2. register child in the parent live-child set
3. create child stepper with inherited env and fresh abort token
4. start child machine
5. return a persistent `Fiber<'t, 'e>` handle

A child removes itself from the parent live-child set immediately when it completes.
The `Fiber` handle remains usable after completion because the handle stores the final exit.

This gives the key structured-concurrency guarantee:

> A parent fiber is not fully complete until its own computation has completed
> and all still-live descendants have either completed or been aborted and cleaned up.

That guarantee prevents parent `bracket` / `defer` cleanup from outrunning child work that still depends on parent-owned resources.

There is no detach / daemon mode in this design. Every forked fiber is strictly bound to its parent.

---

## How fork and forkOn Work

Both primitives:

1. Create a child handle with fresh abort token source
2. Register it in the current fiber's live-child set
3. Create a child `RuntimeStepper` with the same env and child handle
4. Build the child `TypedMachine`
5. Start `runTaskLoop` for the child machine
6. Return a persistent `Fiber<'t, 'e>` wrapping the child handle

The difference is step 5:

- **`fork`**: start `runTaskLoop` directly. The child begins on the caller's thread and yields at the first async boundary.
- **`forkOn`**: start via `Task.Run`. The child begins on a ThreadPool thread immediately.

### When to use which

| Situation | Use |
|---|---|
| Network I/O, file I/O, async operations | `fork` |
| CPU-heavy computation | `forkOn` |
| Blocking FFI / native interop | `forkOn` |
| Most code | `fork` |

---

## Cancellation

### Meaning

`Aborted` means manual or scope-driven cancellation:

- `Fiber.abort fiber`
- parent completion aborting still-live children
- `race` aborting the loser
- `all` aborting remaining children after the first non-success
- `timeout` aborting the timed-out child

`Aborted` is not:

- a managed error
- a defect
- a timeout

### Mechanism

Each fiber owns a `CancellationTokenSource`. The token flows through the fiber's `RuntimeStepper`. Cancellation is checked at two points:

1. **Between steps in `stepEff`**: at the top of the stepping loop, `stepper.Token.IsCancellationRequested` is checked. If true, the current frames are unwound with `BoxedAborted`. Cleanup (`defer` / `bracket`) runs normally through the unwind path.

2. **During async awaits in `runTaskLoop`**: `taskObj.WaitAsync(token)` breaks out of a parked await when the token fires. The continuation maps that to `BoxedAborted`.

### Abort is a 4th channel, not a defect

Abort is intentional. It is not an error and not a defect. It gets its own exit channel:

```fsharp
// Internal:
type BoxedExit =
  | BoxedOk of ok: obj
  | BoxedErr of err: obj
  | BoxedExn of exn: exn
  | BoxedAborted

// Public:
type Exit<'t, 'e> =
  | Ok of 't
  | Err of 'e
  | Exn of exn
  | Aborted
```

Frame handling for abort:

| Frame | HandleAborted behavior |
|---|---|
| MapFrame | propagate |
| FlatMapFrame (bind) | propagate |
| MapErrFrame | propagate |
| FlatMapErrFrame (`orElseWith`) | propagate |
| FlatMapExnFrame (`catch`) | propagate |
| DeferFrame (`ensure`) | run cleanup, then propagate |
| BracketReleaseFrame | run release, then propagate |
| BracketAcquireFrame | propagate |
| DeferScopeFrame | propagate |

The critical property: **manual cancellation is not catchable.**
`Eff.catch` does not intercept `Aborted`.
Only cleanup frames execute during abort unwind.

If cleanup itself fails during abort unwind, cleanup failure takes precedence.

### Fiber.abort semantics

```text
1. fiber.CTS.Cancel()   -- signal the child to stop
2. await fiber.Task     -- wait for cleanup to finish
3. return ()            -- already-completed fiber is a silent no-op
```

Abort waits for the child's task to complete.
The handle owns disposal and lifecycle state; `abort` does not blindly dispose shared runtime state from arbitrary call sites.

---

## Root Scope

The root runner is just the root fiber.
When the root computation exits, the runtime must still abort and await any live child fibers in the root child set before the overall run is complete.

```text
runTask env program
  |
  |- root effect runs
  |  |- fork childA        -> registered under root
  |  |- fork childB        -> registered under root
  |  |  \- fork grandchild -> registered under childB
  |  \- root completes
  |
  |- abort remaining live children
  |- await all cleanup
  \- return final Exit
```

If child shutdown cleanup fails during root completion, that cleanup failure overrides the root's original exit.
If multiple child cleanups fail during shutdown, the first failure wins.

---

## Fiber Handle Shape

```fsharp
type FiberHandle<'t, 'e> =
  { Parent: FiberHandle<obj, obj> option
    Cts: CancellationTokenSource
    MutableState: FiberState<'t, 'e>
    LiveChildren: LiveChildSet
    Completion: Task<BoxedExit> }
```

The exact representation can vary, but it must support:

- direct-child registration and deregistration
- persistent final exit storage
- idempotent abort
- exactly-once completion
- safe shutdown of live children

The parent's child structure tracks only live children, not historical children.
That keeps memory proportional to live concurrency rather than total historical forks.

---

## Runtime Changes

### Stepper interface

```fsharp
and Stepper<'env> =
  abstract Env: 'env
  abstract Token: CancellationToken
  abstract CurrentFiber: FiberHandle<obj, obj>
  abstract Project<'inner>: ('env -> 'inner) -> Stepper<'inner>
  abstract Fork: FiberHandle<obj, obj> -> CancellationToken -> Stepper<'env>
  abstract Step<'t, 'e>: Eff<'t, 'e, 'env> -> Frame<'env> list -> StepResult<'env>
  abstract Unwind: BoxedExit -> Frame<'env> list -> StepResult<'env>
```

- `Project` stays in the same fiber
- `Fork` moves to a new child fiber with a fresh token and child handle

### RuntimeStepper

```fsharp
type RuntimeStepper<'env>(env: 'env, token: CancellationToken, currentFiber: FiberHandle<obj, obj>) as this =
  interface Stepper<'env> with
    member _.Env = env
    member _.Token = token
    member _.CurrentFiber = currentFiber
    member _.Project(project) =
      RuntimeStepper<'inner>(project env, token, currentFiber) :> Stepper<'inner>
    member _.Fork(childFiber, childToken) =
      RuntimeStepper<'env>(env, childToken, childFiber) :> Stepper<'env>
    member _.Step inner frames = stepEff (this :> Stepper<'env>) inner frames
    member _.Unwind exit frames = unwind (this :> Stepper<'env>) exit frames
```

### stepEff: cancellation check between steps

```fsharp
let stepEff<'t, 'e, 'env> (stepper: Stepper<'env>) (eff: Eff<'t, 'e, 'env>) (frames: Frame<'env> list) =
  ...
  while not finished do
    if stepper.Token.IsCancellationRequested then
      result <- ValueSome(unwind stepper BoxedAborted currentFrames)
      finished <- true
    else
      match currentEff with
      | ...
      | Eff.Task tsk ->
        try
          let awaited = task { let! value = tsk () in return box value }
          result <-
            ValueSome(
              Await(
                awaited,
                function
                | Ok value ->
                  if stepper.Token.IsCancellationRequested then
                    unwind stepper BoxedAborted currentFrames
                  else
                    unwind stepper (BoxedOk value) currentFrames
                | Error ex ->
                  if stepper.Token.IsCancellationRequested then
                    unwind stepper BoxedAborted currentFrames
                  else
                    unwind stepper (BoxedExn ex) currentFrames
              )
            )
        ...
```

### runTaskLoop: WaitAsync for responsive cancellation

```fsharp
let runTaskLoop (machine: Machine) (token: CancellationToken) : Task<BoxedExit> =
  task {
    ...
    while not finished do
      match current.Poll() with
      | MachineDone value -> ...
      | MachineAwait(taskObj, cont) ->
        try
          let! value = taskObj.WaitAsync(token)
          current <- cont (Ok value)
        with ex ->
          current <- cont (Error ex)
      | MachineSwitch(machine, resume) -> ...
  }
```

`WaitAsync(token)` breaks out of a parked await when the token is cancelled. The continuation maps that to `BoxedAborted`.

### unwind: handle BoxedAborted

```fsharp
let unwind (stepper: Stepper<'env>) (exit: BoxedExit) (frames: Frame<'env> list) =
  ...
  | frame :: rest ->
    let action =
      match currentExit with
      | BoxedOk value -> frame.HandleOk(value, rest)
      | BoxedErr err -> frame.HandleErr(err, rest)
      | BoxedExn ex -> frame.HandleExn(ex, rest)
      | BoxedAborted -> frame.HandleAborted(rest)
```

### Frame: new HandleAborted member

```fsharp
and [<AbstractClass>] Frame<'env>() =
  abstract HandleOk: obj * Frame<'env> list -> UnwindAction<'env>
  abstract HandleErr: obj * Frame<'env> list -> UnwindAction<'env>
  abstract HandleExn: exn * Frame<'env> list -> UnwindAction<'env>
  abstract HandleAborted: Frame<'env> list -> UnwindAction<'env>
```

All existing frames gain a `HandleAborted` that propagates:

```fsharp
// MapFrame, FlatMapFrame, MapErrFrame, FlatMapErrFrame,
// FlatMapExnFrame, BracketAcquireFrame, DeferScopeFrame:
override _.HandleAborted(rest) =
  ContinueWithExit(BoxedAborted, rest)

// DeferFrame (ensure) -- cleanup runs, then propagates:
override _.HandleAborted(rest) =
  RunCleanup(
    (BoxedEff<unit, 'e, 'env>(cleanup) :> BoxedEff<'env>),
    function
    | BoxedOk _ -> ContinueWithExit(BoxedAborted, rest)
    | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
    | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
    | BoxedAborted -> ContinueWithExit(BoxedAborted, rest)
  )

// BracketReleaseFrame -- release runs, then propagates:
override _.HandleAborted(rest) =
  runCleanup rest (function
    | BoxedOk _ -> ContinueWithExit(BoxedAborted, rest)
    | BoxedErr cleanupErr -> ContinueWithExit(BoxedErr cleanupErr, rest)
    | BoxedExn cleanupExn -> ContinueWithExit(BoxedExn cleanupExn, rest)
    | BoxedAborted -> ContinueWithExit(BoxedAborted, rest)
  )
```

### RunCleanup continuation: handle BoxedAborted

```fsharp
| RunCleanup(cleanup, cont) ->
  let cleanupFrame =
    { new Frame<'env>() with
        member _.HandleOk(value, _) = cont (BoxedOk value)
        member _.HandleErr(err, _) = cont (BoxedErr err)
        member _.HandleExn(ex, _) = cont (BoxedExn ex)
        member _.HandleAborted(_) = cont BoxedAborted
    }
  result <- ValueSome(Continue(cleanup, [ cleanupFrame ]))
```

### runTask: create root fiber, cleanup on exit

```fsharp
let runTask (env: 'env) (eff: Eff<'t, 'e, 'env>) : Task<Exit<'t, 'e>> =
  let rootFiber = FiberHandle.root()
  let stepper =
    RuntimeStepper<'env>(env, CancellationToken.None, rootFiber :> FiberHandle<obj, obj>) :> Stepper<'env>
  let machine =
    TypedMachine<'env>(stepper, stepper.Step eff []) :> Machine

  task {
    let! exit = runTaskLoop machine CancellationToken.None
    let! finalExit = rootFiber.CompleteRoot(exit)

    match finalExit with
    | BoxedOk value -> return Exit.Ok(unbox<'t> value)
    | BoxedErr err -> return Exit.Err(unbox<'e> err)
    | BoxedExn ex -> return Exit.Exn ex
    | BoxedAborted -> return Exit.Aborted
  }
```

---

## New Nodes

### Fork

```fsharp
type Fork<'t, 'e, 'e2, 'env>(body: Eff<'t, 'e, 'env>, useTaskRun: bool) =
  inherit Node<Fiber<'t, 'e>, 'e2, 'env>()

  interface INodeRuntime<'env> with
    member _.Enter(stepper, frames) =
      let cts = new CancellationTokenSource()
      let childHandle = FiberHandle.child(stepper.CurrentFiber, cts)
      stepper.CurrentFiber.RegisterChild(childHandle)
      let childStepper = stepper.Fork(childHandle, cts.Token)
      let childMachine =
        TypedMachine<'env>(childStepper, childStepper.Step body []) :> Machine

      let childTask =
        if useTaskRun then
          Task.Run(fun () -> runTaskLoop childMachine cts.Token)
        else
          runTaskLoop childMachine cts.Token

      childHandle.AttachTask(childTask)
      let fiber = Fiber<'t, 'e>(childHandle)
      unwind stepper (BoxedOk(box fiber)) frames
```

### AwaitFiber

Observes the child's exit as data:

```fsharp
type AwaitFiber<'t, 'e, 'env>(fiber: Fiber<'t, 'e>) =
  inherit Node<Exit<'t, 'e>, never, 'env>()

  interface INodeRuntime<'env> with
    member _.Enter(stepper, frames) =
      Await(
        task { let! exit = fiber.Handle.Task in return box exit },
        function
          | Ok boxedResult ->
            let exit = unbox<BoxedExit> boxedResult
            let typedExit =
              match exit with
              | BoxedOk v -> Exit.Ok(unbox<'t> v)
              | BoxedErr e -> Exit.Err(unbox<'e> e)
              | BoxedExn ex -> Exit.Exn ex
              | BoxedAborted -> Exit.Aborted
            stepper.Unwind (BoxedOk(box typedExit)) frames
          | Error ex ->
            stepper.Unwind (BoxedExn ex) frames
      )
```

### JoinFiber

Adopts the child's exit into the caller:

```fsharp
type JoinFiber<'t, 'e, 'e2, 'env>(fiber: Fiber<'t, 'e>) =
  inherit Node<'t, 'e, 'env>()

  interface INodeRuntime<'env> with
    member _.Enter(stepper, frames) =
      Await(
        task { let! exit = fiber.Handle.Task in return box exit },
        function
          | Ok boxedResult ->
            let exit = unbox<BoxedExit> boxedResult
            stepper.Unwind exit frames
          | Error ex ->
            stepper.Unwind (BoxedExn ex) frames
      )
```

### AbortFiber

```fsharp
type AbortFiber<'t, 'e, 'e2, 'env>(fiber: Fiber<'t, 'e>) =
  inherit Node<unit, 'e2, 'env>()

  interface INodeRuntime<'env> with
    member _.Enter(stepper, frames) =
      fiber.Handle.RequestAbort()
      Await(
        task {
          let! _ = fiber.Handle.Task
          return box ()
        },
        function
          | Ok _ -> stepper.Unwind (BoxedOk(box ())) frames
          | Error ex -> stepper.Unwind (BoxedExn ex) frames
      )
```

If the fiber is already complete, `RequestAbort()` is a silent no-op.

---

## Derived Combinator Implementations

### race

Fork both effects, `Task.WhenAny` to find the winner, abort the loser, await loser cleanup, route the winner's exit:

```fsharp
let race (a: Eff<'t, 'e, 'env>) (b: Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> =
  Eff.Node(RaceNode(a, b))
```

Internally, `RaceNode`:

1. starts both child fibers
2. awaits `Task.WhenAny`
3. identifies winner and loser
4. aborts loser
5. awaits loser cleanup
6. routes winner exit

If the winner is `Aborted`, the whole race is `Aborted`.
If loser cleanup fails, that cleanup failure overrides the winner.

### all

Fork all effects and await all completions:

1. Start all as child tasks
2. Await tasks in a completion loop
3. On first non-success (`Err`, `Exn`, or `Aborted`): abort remaining children, await cleanup, route that exit
4. On all success: collect results in input order and route `BoxedOk(box resultList)`

### timeout

Fork effect plus `Task.Delay`, then race between them:

1. Start effect as child task
2. Create `Task.Delay(duration)`
3. `Task.WhenAny(effectTask, delayTask)`
4. If delay wins:
   - abort effect fiber
   - await cleanup
   - if cleanup succeeds, return `TimedOut`
   - if cleanup fails, route that `Err` / `Exn`
5. If effect wins:
   - `Ok value` -> `Completed value`
   - `Err e` -> `Err e`
   - `Exn ex` -> `Exn ex`
   - `Aborted` -> `Aborted`

---

## Performance Impact

### Non-forked effects (existing code, no fork calls)

| Change | Cost |
|---|---|
| `RuntimeStepper` constructor takes `CancellationToken` + current fiber handle | 0 -- stored as fields |
| `stepEff` checks `stepper.Token.IsCancellationRequested` each step | effectively zero when token is `CancellationToken.None` |
| `runTaskLoop` calls `taskObj.WaitAsync(CancellationToken.None)` | effectively zero on the non-cancellable fast path |
| `Frame` gains `HandleAborted` abstract member | no cost unless used |
| root fiber handle created in `runTask` | small constant allocation |
| root child shutdown on exit with no children | ~0 |

### Per-fiber overhead

Per fiber there is still a real cost:

- `CancellationTokenSource`
- `Task` async state machine
- child `RuntimeStepper`
- `TypedMachine` + initial `StepResult`
- handle lifecycle state
- live direct-child set

This is somewhat more bookkeeping than a flat runner-wide bag, but the difference is still dominated by `Task`, CTS, and actual user work.

### Why the tree still scales better

The parent tree costs slightly more bookkeeping than a flat global bag, but it scales better semantically and operationally because:

- completed children do not remain in parent state
- shutdown scans live children, not historical children
- memory stays proportional to live concurrency, not total historical forks
- resource ownership stays aligned with runtime structure

For very high fiber counts, immediate deregistration is the critical property.

---

## Env Safety

`fork` starts the child's `task { }` on the caller's thread. It runs synchronously until the first await. After that, the continuation runs on a ThreadPool thread. Multiple forked fibers may execute simultaneously on different threads.

This means **forked fibers access the env concurrently.**
The env must be safe for concurrent access patterns used by your capabilities.

In the typical EffSharp pattern, envs are bags of capability providers:

```fsharp
type AppEnv() =
  interface Effect.Stdio with
    member _.Stdio = Stdio.Provider()
  interface Effect.Clock with
    member _.Clock = Clock.Provider()
```

These are safe for concurrent access if the provided capabilities are themselves concurrency-safe. This is the expected pattern.

If a user puts mutable shared state in the env, concurrent access is their responsibility.

---

## Example Usage

### Fork and await

```fsharp
let program () = eff {
  let! fiberA = Eff.fork (fetchUser userId)
  let! fiberB = Eff.fork (fetchPosts userId)
  let! userExit = Fiber.await fiberA
  let! postsExit = Fiber.await fiberB
  let user = Exit.ok userExit
  let posts = Exit.ok postsExit
  return user, posts
}
```

### Fork and join

```fsharp
let program () = eff {
  let! fiberA = Eff.fork (fetchUser userId)
  let! fiberB = Eff.fork (fetchPosts userId)
  let! user = Fiber.join fiberA
  let! posts = Fiber.join fiberB
  return user, posts
}
```

### Race

```fsharp
let fetchFromFastest url = eff {
  let! response = Eff.race (fetchFromCdn url) (fetchFromOrigin url)
  return response
}
```

### Timeout

```fsharp
let fetchWithTimeout url = eff {
  let! result = Eff.timeout (TimeSpan.FromSeconds 5.0) (Http.get url)
  match result with
  | Completed response -> return response
  | TimedOut -> return! Err (TimedOut url)
}
```

### Server accept loop

```fsharp
let server () = eff {
  do! Net.withListener (addr 8080) 128 (fun listener -> eff {
    while true do
      let! stream, remote = Net.tcpAccept listener
      let! _ = Eff.fork (handleConnection stream remote)
      ()
  })
}
// When the listener closes (or the runner exits),
// all live forked connection handlers are aborted + cleaned up
// before listener cleanup is considered complete.
```

### CPU-bound work on ThreadPool

```fsharp
let program () = eff {
  let! fiber = Eff.forkOn (eff {
    return expensiveComputation data
  })
  do! otherWork ()
  let! result = Fiber.join fiber
  return result
}
```

---

## Semantic Summary

- `Ok` / `Err` / `Exn` / `Aborted` are the four execution outcomes
- `Aborted` is manual or scope cancellation, not exception
- timeout is separate from abort
- `await` observes exit as data
- `join` adopts child outcome
- abort is not catchable by ordinary defect handlers
- cleanup failure overrides abort
- root shutdown cleanup failure overrides root exit
- first cleanup failure wins when multiple shutdown cleanups fail
- every fiber is strictly bound to its parent
