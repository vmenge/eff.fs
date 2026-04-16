namespace EffSharp.Core.Tests

module Concurrency =
  open System
  open System.Threading
  open System.Threading.Tasks
  open Expecto
  open EffSharp.Core

  let private gate<'t> () =
    TaskCompletionSource<'t>(TaskCreationOptions.RunContinuationsAsynchronously)

  let private record (gate: obj) (events: ResizeArray<string>) event =
    lock gate (fun () -> events.Add event)

  let tests =
    testList "Concurrency" [
      testTask "await returns Exit.Aborted after abort" {
        let entered = gate<unit> ()
        let never = gate<int> ()

        let! result =
          eff {
            let! fiber =
              Eff.fork
              <| eff {
                do! entered.Task
                let! value = never.Task
                return value
              }

            entered.TrySetResult(()) |> ignore
            do! Fiber.abort fiber
            return! Fiber.await fiber
          }
          |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Ok(Exit.Aborted: Exit<int, unit>))
          "await should observe an aborted child as Exit.Aborted"
      }

      testTask "join propagates Aborted and catch does not intercept it" {
        let entered = gate<unit> ()
        let never = gate<int> ()

        let! result =
          eff {
            let! fiber =
              Eff.fork
              <| eff {
                do! entered.Task
                let! value = never.Task
                return value
              }

            entered.TrySetResult(()) |> ignore
            do! Fiber.abort fiber
            return! Fiber.join fiber
          }
          |> Eff.catch (fun _ -> Pure 99)
          |> Eff.runTask ()

        Expect.equal
          result
          Exit.Aborted
          "manual abort should bypass catch and propagate through join"
      }

      testTask "abort is a no-op after child already failed" {
        let! forked =
          eff {
            let! fiber = Eff.fork (Err "boom")
            return fiber
          }
          |> Eff.runTask ()

        let fiber =
          match forked with
          | Exit.Ok fiber -> fiber
          | other -> failtest $"expected fork to succeed, got %A{other}"

        let! observed = Fiber.await fiber |> Eff.runTask ()

        Expect.equal
          observed
          (Exit.Ok(Exit.Err "boom"))
          "the child should already be completed with its original error"

        let! result = Fiber.abort fiber |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Ok())
          "abort should be idempotent and succeed even after the child already failed"
      }

      testTask "abort is a no-op after child already defects" {
        let boom = exn "boom"

        let! forked =
          eff {
            let! fiber = Eff.fork (Eff.thunk (fun () -> raise boom))
            return fiber
          }
          |> Eff.runTask ()

        let fiber =
          match forked with
          | Exit.Ok fiber -> fiber
          | other -> failtest $"expected fork to succeed, got %A{other}"

        let! observed = Fiber.await fiber |> Eff.runTask ()

        match observed with
        | Exit.Ok(Exit.Exn ex) ->
          Expect.equal
            ex.Message
            "boom"
            "the child should already be completed with its original defect"
        | other -> failtest $"expected child defect, got %A{other}"

        let! result = Fiber.abort fiber |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Ok())
          "abort should be idempotent and succeed even after the child already defected"
      }

      testTask "forkOn runs child and join returns its value" {
        let! result =
          Eff.sleep (TimeSpan.FromMilliseconds 1L)
          |> Eff.map (fun _ -> 42)
          |> Eff.forkOn
          |> Eff.bind Fiber.join
          |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Ok 42)
          "forkOn should run the child on a background task"
      }

      testTask "abort waits for child cleanup before returning" {
        let entered = gate<unit> ()
        let cleanupStarted = gate<unit> ()
        let cleanupRelease = gate<unit> ()
        let events = ResizeArray<string>()
        let eventsGate = obj ()

        let releaseCleanup = task {
          do! cleanupStarted.Task
          record eventsGate events "cleanup-observed"
          cleanupRelease.SetResult(())
        }

        let never = gate<int> ()

        let child =
          eff {
            do! Eff.thunk (fun () -> record eventsGate events "child-start")
            do! Eff.thunk (fun () -> entered.TrySetResult(()) |> ignore)
            let! value = never.Task
            return value
          }
          |> Eff.ensure (
            eff {
              do!
                Eff.thunk
                <| fun () -> record eventsGate events "child-cleanup-start"

              do!
                Eff.thunk <| fun () -> cleanupStarted.TrySetResult(()) |> ignore

              do! cleanupRelease.Task

              do!
                Eff.thunk
                <| fun () -> record eventsGate events "child-cleanup-end"
            }
          )

        let! result =
          eff {
            let! fiber = Eff.fork child
            do! entered.Task
            do! Fiber.abort fiber
            do! Eff.thunk (fun () -> record eventsGate events "after-abort")
          }
          |> Eff.runTask ()

        do! releaseCleanup

        Expect.equal result (Exit.Ok()) "abort should complete successfully"

        Expect.sequenceEqual
          events
          [
            "child-start"
            "child-cleanup-start"
            "cleanup-observed"
            "child-cleanup-end"
            "after-abort"
          ]
          "abort should not return until child cleanup has finished"
      }

      testTask "forkOn abort waits for child cleanup before returning" {
        let entered = gate<unit> ()
        let cleanupStarted = gate<unit> ()
        let cleanupRelease = gate<unit> ()
        let events = ResizeArray<string>()
        let eventsGate = obj ()

        let releaseCleanup = task {
          do! cleanupStarted.Task
          record eventsGate events "cleanup-observed"
          cleanupRelease.SetResult(())
        }

        let never = gate<int> ()

        let child =
          eff {
            do! Eff.thunk (fun () -> record eventsGate events "child-start")
            do! Eff.thunk (fun () -> entered.TrySetResult(()) |> ignore)
            let! value = never.Task
            return value
          }
          |> Eff.ensure (
            eff {
              do!
                Eff.thunk (fun () ->
                  record eventsGate events "child-cleanup-start"
                )

              do!
                Eff.thunk (fun () -> cleanupStarted.TrySetResult(()) |> ignore)

              do! cleanupRelease.Task

              do!
                Eff.thunk (fun () ->
                  record eventsGate events "child-cleanup-end"
                )
            }
          )

        let! result =
          eff {
            let! fiber = Eff.forkOn child
            do! entered.Task
            do! Fiber.abort fiber
            do! Eff.thunk (fun () -> record eventsGate events "after-abort")
          }
          |> Eff.runTask ()

        do! releaseCleanup

        Expect.equal
          result
          (Exit.Ok())
          "forkOn abort should complete successfully"

        Expect.sequenceEqual
          events
          [
            "child-start"
            "child-cleanup-start"
            "cleanup-observed"
            "child-cleanup-end"
            "after-abort"
          ]
          "forkOn abort should not return until child cleanup has finished"
      }

      testTask "abort waits for child defer cleanup before returning" {
        let entered = gate<unit> ()
        let cleanupStarted = gate<unit> ()
        let cleanupRelease = gate<unit> ()
        let events = ResizeArray<string>()
        let eventsGate = obj ()

        let releaseCleanup = task {
          do! cleanupStarted.Task
          record eventsGate events "defer-observed"
          cleanupRelease.SetResult(())
        }

        let never = gate<int> ()

        let child = eff {
          defer (
            eff {
              do!
                Eff.thunk (fun () ->
                  record eventsGate events "child-defer-start"
                )

              do!
                Eff.thunk (fun () -> cleanupStarted.TrySetResult(()) |> ignore)

              do! cleanupRelease.Task

              do!
                Eff.thunk (fun () -> record eventsGate events "child-defer-end")
            }
          )

          do! Eff.thunk (fun () -> record eventsGate events "child-start")
          do! Eff.thunk (fun () -> entered.TrySetResult(()) |> ignore)
          let! value = never.Task
          return value
        }

        let! result =
          eff {
            let! fiber = Eff.fork child
            do! entered.Task
            do! Fiber.abort fiber
            do! Eff.thunk (fun () -> record eventsGate events "after-abort")
          }
          |> Eff.runTask ()

        do! releaseCleanup

        Expect.equal result (Exit.Ok()) "abort should complete successfully"

        Expect.sequenceEqual
          events
          [
            "child-start"
            "child-defer-start"
            "defer-observed"
            "child-defer-end"
            "after-abort"
          ]
          "abort should not return until child defer cleanup has finished"
      }

      testTask "timeout returns TimedOut when child cleanup succeeds" {
        let mutable cleaned = false
        let never = gate<int> ()

        let! result =
          Eff.timeout
            (TimeSpan.FromMilliseconds 25.0)
            (eff {
              let! value = never.Task
              return value
             }
             |> Eff.ensure (Eff.thunk (fun () -> cleaned <- true)))
          |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Ok(TimedOut: TimeoutResult<int>))
          "timeout should be a distinct result, not Aborted"

        Expect.isTrue cleaned "timed out children should still run cleanup"
      }

      testTask "timeout cleanup failure overrides TimedOut" {
        let never = gate<int> ()

        let! result =
          Eff.timeout
            (TimeSpan.FromMilliseconds 25.0)
            (eff {
              let! value = never.Task
              return value
             }
             |> Eff.ensure (Err "cleanup failed"))
          |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Err "cleanup failed")
          "cleanup failure should override TimedOut"
      }

      testTask
        "timeout aborts the child before it can complete and returns TimedOut" {
        let entered = gate<unit> ()
        let mutable cleaned = false
        let never = gate<int> ()

        let! result =
          Eff.timeout
            (TimeSpan.FromMilliseconds 25.0)
            (eff {
              do! Eff.thunk (fun () -> entered.TrySetResult(()) |> ignore)
              let! value = never.Task
              return value
             }
             |> Eff.ensure (Eff.thunk (fun () -> cleaned <- true)))
          |> Eff.runTask ()

        do! entered.Task

        Expect.equal
          result
          (Exit.Ok(TimedOut: TimeoutResult<int>))
          "timeout should return TimedOut when the timer wins and cleanup succeeds"

        Expect.isTrue
          cleaned
          "timeout should abort the child and run cleanup before returning TimedOut"
      }

      testTask
        "timeout is not overridden by child defecting after timeout abort was requested" {
        let childRelease = new ManualResetEventSlim(false)
        let childBlocked = gate<unit> ()

        try
          let timeoutTask =
            Eff.timeout
              (TimeSpan.FromMilliseconds 25.0)
              (eff {
                let! _ = task {
                  do! Task.Delay 1
                  return ()
                }

                do!
                  Eff.thunk (fun () -> childBlocked.TrySetResult(()) |> ignore)

                return!
                  Eff.thunk (fun () ->
                    childRelease.Wait()
                    raise (exn "boom")
                  )
              })
            |> Eff.runTask ()

          do! childBlocked.Task
          do! Task.Delay 50
          childRelease.Set()

          let! result = timeoutTask

          Expect.equal
            result
            (Exit.Ok(TimedOut: TimeoutResult<int>))
            "timeout should stay TimedOut when the child defects only after timeout-triggered abort was requested"
        finally
          childRelease.Dispose()
      }

      testTask "race waits for loser cleanup before returning winner" {
        let cleanupStarted = gate<unit> ()
        let cleanupRelease = gate<unit> ()
        let mutable cleaned = false

        let releaseCleanup = task {
          do! cleanupStarted.Task
          cleanupRelease.SetResult(())
        }

        let never = gate<int> ()

        let loser =
          eff {
            let! value = never.Task
            return value
          }
          |> Eff.ensure (
            eff {
              do!
                Eff.thunk (fun () -> cleanupStarted.TrySetResult(()) |> ignore)

              do! cleanupRelease.Task
              do! Eff.thunk (fun () -> cleaned <- true)
            }
          )

        let! result = Eff.race (Pure 1) loser |> Eff.runTask ()

        do! releaseCleanup

        Expect.equal result (Exit.Ok 1) "race should return the winning value"
        Expect.isTrue cleaned "race should await loser cleanup before returning"
      }

      testTask "race returns the first completed error" {
        let slowSuccess = eff {
          let! value = task {
            do! Task.Delay 25
            return 1
          }

          return value
        }

        let! result = Eff.race (Err "boom") slowSuccess |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Err "boom")
          "race should route the first completed exit even when that exit is an error"
      }

      testTask "race returns the first completed defect" {
        let slowSuccess = eff {
          let! value = task {
            do! Task.Delay 25
            return 1
          }

          return value
        }

        let boom = exn "boom"

        let! result =
          Eff.race (Eff.thunk (fun () -> raise boom)) slowSuccess
          |> Eff.runTask ()

        match result with
        | Exit.Exn ex ->
          Expect.equal
            ex.Message
            "boom"
            "race should route the first completed defect"
        | other -> failtest $"expected race to return defect, got %A{other}"
      }

      testTask
        "race winner is not overridden by loser defecting after abort was requested" {
        let loserRelease = new ManualResetEventSlim(false)
        let loserBlocked = gate<unit> ()
        let winnerRelease = gate<unit> ()

        try
          let winner = eff {
            do! winnerRelease.Task
            return 1
          }

          let loser = eff {
            let! _ = task {
              do! Task.Delay 1
              return ()
            }

            do! Eff.thunk (fun () -> loserBlocked.TrySetResult(()) |> ignore)

            return!
              Eff.thunk (fun () ->
                loserRelease.Wait()
                raise (exn "boom")
              )
          }

          let raceTask = Eff.race winner loser |> Eff.runTask ()

          do! loserBlocked.Task
          winnerRelease.SetResult(())
          do! Task.Delay 25
          loserRelease.Set()

          let! result = raceTask

          Expect.equal
            result
            (Exit.Ok 1)
            "race should keep the winner even if the loser defects after abort was requested"
        finally
          loserRelease.Dispose()
      }

      testTask "all aborts siblings on error and waits for cleanup" {
        let cleanupStarted = gate<unit> ()
        let cleanupRelease = gate<unit> ()
        let mutable cleaned = false

        let releaseCleanup = task {
          do! cleanupStarted.Task
          cleanupRelease.SetResult(())
        }

        let never = gate<int> ()

        let blocked =
          eff {
            let! value = never.Task
            return value
          }
          |> Eff.ensure (
            eff {
              do!
                Eff.thunk (fun () -> cleanupStarted.TrySetResult(()) |> ignore)

              do! cleanupRelease.Task
              do! Eff.thunk (fun () -> cleaned <- true)
            }
          )

        let! result = Eff.all [ blocked; Err "boom" ] |> Eff.runTask ()

        do! releaseCleanup

        Expect.equal
          result
          (Exit.Err "boom")
          "all should surface the first terminal exit"

        Expect.isTrue
          cleaned
          "all should await aborted sibling cleanup before returning"
      }

      testTask "all returns the first terminal error" {
        let slowSuccess = eff {
          let! value = task {
            do! Task.Delay 25
            return 2
          }

          return value
        }

        let! result = Eff.all [ Err "boom"; slowSuccess ] |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Err "boom")
          "all should fail with the first terminal non-success and abort the rest"
      }

      testTask "all returns the first terminal defect" {
        let slowSuccess = eff {
          let! value = task {
            do! Task.Delay 25
            return 2
          }

          return value
        }

        let boom = exn "boom"

        let! result =
          Eff.all [ Eff.thunk (fun () -> raise boom); slowSuccess ]
          |> Eff.runTask ()

        match result with
        | Exit.Exn ex ->
          Expect.equal
            ex.Message
            "boom"
            "all should fail with the first terminal defect"
        | other -> failtest $"expected all to return defect, got %A{other}"
      }

      testTask
        "all first failure is not overridden by later sibling defecting after abort was requested" {
        let laterRelease = new ManualResetEventSlim(false)
        let laterBlocked = gate<unit> ()

        try
          let firstFailure = eff {
            do! laterBlocked.Task
            return! Err "first"
          }

          let laterSibling = eff {
            let! _ = task {
              do! Task.Delay 1
              return ()
            }

            do! Eff.thunk (fun () -> laterBlocked.TrySetResult(()) |> ignore)

            return!
              Eff.thunk (fun () ->
                laterRelease.Wait()
                raise (exn "boom")
              )
          }

          let allTask = Eff.all [ firstFailure; laterSibling ] |> Eff.runTask ()

          do! laterBlocked.Task
          do! Task.Delay 25
          laterRelease.Set()

          let! result = allTask

          Expect.equal
            result
            (Exit.Err "first")
            "all should keep the first terminal failure even if an aborted sibling defects later"
        finally
          laterRelease.Dispose()
      }

      testTask "all returns results in input order on success" {
        let! result = Eff.all [ Pure 1; Pure 2; Pure 3 ] |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Ok [ 1; 2; 3 ])
          "all should preserve input order"
      }

      testTask "parent cleanup waits for forked child cleanup" {
        let childCleanupStarted = gate<unit> ()
        let childCleanupRelease = gate<unit> ()
        let events = ResizeArray<string>()
        let eventsGate = obj ()

        let releaseChildCleanup = task {
          do! childCleanupStarted.Task
          record eventsGate events "child-cleanup-observed"
          childCleanupRelease.SetResult(())
        }

        let never = gate<int> ()

        let child =
          eff {
            do! Eff.thunk (fun () -> record eventsGate events "child-start")
            let! value = never.Task
            return value
          }
          |> Eff.ensure (
            eff {
              do!
                Eff.thunk (fun () ->
                  record eventsGate events "child-cleanup-start"
                )

              do!
                Eff.thunk (fun () ->
                  childCleanupStarted.TrySetResult(()) |> ignore
                )

              do! childCleanupRelease.Task

              do!
                Eff.thunk (fun () ->
                  record eventsGate events "child-cleanup-end"
                )
            }
          )

        let! result =
          (eff {
            let! _fiber = Eff.fork child
            do! Eff.thunk (fun () -> record eventsGate events "parent-body-end")
            return ()
          })
          |> Eff.ensure (
            Eff.thunk (fun () -> record eventsGate events "parent-cleanup")
          )
          |> Eff.runTask ()

        do! releaseChildCleanup

        Expect.equal result (Exit.Ok()) "root run should still succeed"

        Expect.sequenceEqual
          events
          [
            "child-start"
            "parent-body-end"
            "child-cleanup-start"
            "child-cleanup-observed"
            "child-cleanup-end"
            "parent-cleanup"
          ]
          "parent cleanup should not outrun live descendant cleanup"
      }

      testTask "parent cleanup waits for forked child defer cleanup" {
        let childCleanupStarted = gate<unit> ()
        let childCleanupRelease = gate<unit> ()
        let events = ResizeArray<string>()
        let eventsGate = obj ()

        let releaseChildCleanup = task {
          do! childCleanupStarted.Task
          record eventsGate events "child-defer-observed"
          childCleanupRelease.SetResult(())
        }

        let never = gate<int> ()

        let child = eff {
          defer (
            eff {
              do!
                Eff.thunk (fun () ->
                  record eventsGate events "child-defer-start"
                )

              do!
                Eff.thunk (fun () ->
                  childCleanupStarted.TrySetResult(()) |> ignore
                )

              do! childCleanupRelease.Task

              do!
                Eff.thunk (fun () -> record eventsGate events "child-defer-end")
            }
          )

          do! Eff.thunk (fun () -> record eventsGate events "child-start")
          let! value = never.Task
          return value
        }

        let! result =
          (eff {
            let! _fiber = Eff.fork child
            do! Eff.thunk (fun () -> record eventsGate events "parent-body-end")
            return ()
          })
          |> Eff.ensure (
            Eff.thunk (fun () -> record eventsGate events "parent-cleanup")
          )
          |> Eff.runTask ()

        do! releaseChildCleanup

        Expect.equal result (Exit.Ok()) "root run should still succeed"

        Expect.sequenceEqual
          events
          [
            "child-start"
            "parent-body-end"
            "child-defer-start"
            "child-defer-observed"
            "child-defer-end"
            "parent-cleanup"
          ]
          "parent cleanup should not outrun live descendant defer cleanup"
      }
    ]
