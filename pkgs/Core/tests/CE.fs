namespace EffFs.Core.Tests

module CE =
    open System
    open Expecto
    open EffFs.Core
    open System.Threading.Tasks

    type DisposeProbe() =
        let mutable disposed = false

        member _.Disposed = disposed

        interface IDisposable with
            member _.Dispose() = disposed <- true

    type DependencyA =
        abstract SideEffectA: string -> unit

    type DependencyB =
        abstract SideEffectB: string -> unit

    type Env() =
        let arr = ResizeArray()

        member _.Values() = arr.ToArray()

        interface DependencyA with
            member this.SideEffectA(arg1: string) : unit = arg1 |> sprintf "A: %s" |> arr.Add

        interface DependencyB with
            member this.SideEffectB(arg1: string) : unit = arg1 |> sprintf "B: %s" |> arr.Add


    let depASideEffect str =
        Eff.read (fun (env: #DependencyA) -> env.SideEffectA str)

    let depBSideEffect str =
        Eff.read (fun (env: #DependencyB) -> env.SideEffectB str)

    let tests =
        testList
            "CE"
            [ testTask "ce sources works" {
                  let! value =
                      eff {
                          let a = 1
                          let! b = Eff.value 2
                          let! c = Ok 3
                          let! d = Some 4
                          let! e = task { return 5 }
                          let! f = async { return 6 }
                          let! g = task { return Ok 7 }
                          let! h = async { return Ok 8 }
                          let! i = ValueSome 9
                          let! j = ValueTask<int>(10)
                          let! k = ValueTask<Result<int, exn>>(Ok 11)

                          let result = a + b + c + d + e + f + g + h + i + j + k

                          return result
                      }
                      |> Eff.runTask ()

                  Expect.equal value (Ok 66) "should be equal"
              }

              testTask "return! eff works" {
                  let! value = eff { return! Eff.value 5 } |> Eff.runTask ()

                  Expect.equal value (Ok 5) "should return from Eff directly"
              }

              testTask "result error short-circuits" {
                  let mutable ran = false

                  let! value =
                      eff {
                          let! _ = Ok 1
                          let! _ = Error(exn "boom")
                          ran <- true
                          return 1
                      }
                      |> Eff.runTask ()

                  let err: exn = Result.error value
                  Expect.equal err.Message "boom" "should return the result error"
                  Expect.isFalse ran "later CE code should not run"
              }

              testTask "option none short-circuits" {
                  let mutable ran = false

                  let! value =
                      eff {
                          let! _ = Some 1
                          let! _ = None
                          ran <- true
                          return 1
                      }
                      |> Eff.runTask ()

                  let err: exn = Result.error value
                  Expect.equal (err.GetType()) typeof<ValueIsNone> "should return ValueIsNone"
                  Expect.isFalse ran "later CE code should not run"
              }

              testTask "task result error short-circuits" {
                  let mutable ran = false
                  let taskResult () : Task<Result<int, exn>> = task { return Error(exn "boom") }

                  let! value =
                      eff {
                          let! _ = taskResult ()
                          ran <- true
                          return 1
                      }
                      |> Eff.runTask ()

                  let err: exn = Result.error value
                  Expect.equal err.Message "boom" "should return the task result error"
                  Expect.isFalse ran "later CE code should not run"
              }

              testTask "exception thrown inside CE is caught" {
                  let! value =
                      eff {
                          let _ = failwith "boom"
                          return 1
                      }
                      |> Eff.runTask ()

                  let err: exn = Result.error value
                  Expect.equal err.Message "boom" "should catch thrown exceptions"
              }

              testTask "try finally runs on failure" {
                  let mutable cleaned = false

                  let! value =
                      eff {
                          try
                              let! _ = Eff.errwith "boom"
                              return 1
                          finally
                              cleaned <- true
                      }
                      |> Eff.runTask ()

                  let err: exn = Result.error value
                  Expect.equal err.Message "boom" "should preserve the body error"
                  Expect.isTrue cleaned "finally should run"
              }

              testTask "use disposes resources" {
                  let probe = new DisposeProbe()

                  let! value =
                      eff {
                          use _probe = probe
                          return 1
                      }
                      |> Eff.runTask ()

                  Expect.equal value (Ok 1) "should return the body result"
                  Expect.isTrue probe.Disposed "use should dispose the resource"

              }

              testTask "use! disposes resources" {
                  let probe = new DisposeProbe()

                  let! value =
                      eff {
                          use! _probe = Eff.value probe
                          return 1
                      }
                      |> Eff.runTask ()

                  Expect.equal value (Ok 1) "should return the body result"
                  Expect.isTrue probe.Disposed "use! should dispose the resource"
              }

              testTask "defer runs on success" {
                  let mutable cleaned = false

                  let! value =
                      eff {
                          defer (Eff.thunk (fun () -> cleaned <- true))
                          return 1
                      }
                      |> Eff.runTask ()

                  Expect.equal value (Ok 1) "should return the body result"
                  Expect.isTrue cleaned "defer should run on success"
              }

              testTask "defer runs on failure" {
                  let mutable cleaned = false

                  let! value =
                      eff {
                          defer (Eff.thunk (fun () -> cleaned <- true))
                          return! Eff.errwith "boom"
                      }
                      |> Eff.runTask ()

                  let err: exn = Result.error value
                  Expect.equal err.Message "boom" "should preserve the body error"
                  Expect.isTrue cleaned "defer should run on failure"
              }

              testTask "defer runs in LIFO order" {
                  let events = ResizeArray<string>()

                  let! value =
                      eff {
                          defer (Eff.thunk (fun () -> events.Add "outer"))
                          defer (Eff.thunk (fun () -> events.Add "inner"))
                          return 1
                      }
                      |> Eff.runTask ()

                  Expect.equal value (Ok 1) "should return the body result"
                  Expect.sequenceEqual events [ "inner"; "outer" ] "defer should run in LIFO order"
              }

              testTask "defer captures prior locals" {
                  let mutable seen = 0

                  let! value =
                      eff {
                          let x = 41
                          defer (fun () -> seen <- x)
                          let! y = Eff.value 1
                          return x + y
                      }
                      |> Eff.runTask ()

                  Expect.equal value (Ok 42) "should return the body result"
                  Expect.equal seen 41 "defer should capture the local value"
              }

              testTask "defer after let! can use bound value" {
                  let mutable seen = 0

                  let! value =
                      eff {
                          let! x = Eff.value 41
                          defer (Eff.thunk (fun () -> seen <- seen + x))
                          defer (fun () -> seen <- seen + x)
                          return x + 1
                      }
                      |> Eff.runTask ()

                  Expect.equal value (Ok 42) "should return the body result"
                  Expect.equal seen 82 "defer should capture the bound value"
              }

              testTask "multiple defers still run on failure" {
                  let events = ResizeArray<string>()

                  let! value =
                      eff {
                          defer (Eff.thunk (fun () -> events.Add "outer"))
                          defer (Eff.thunk (fun () -> events.Add "inner"))
                          return! Eff.errwith "boom"
                      }
                      |> Eff.runTask ()

                  let err: exn = Result.error value
                  Expect.equal err.Message "boom" "should preserve the body error"
                  Expect.sequenceEqual events [ "inner"; "outer" ] "all defers should run in LIFO order"
              }

              testTask "dep injection" {
                  let value () =
                      eff {
                          do! depASideEffect "yooo"
                          do! depBSideEffect "wazzup"
                      }

                  let env = Env()

                  let! result = value () |> Eff.runTask env

                  Expect.equal (env.Values()) [| "A: yooo"; "B: wazzup" |] "side effects should have been executed"

                  Expect.isTrue (Result.isOk result) "effect should have succeeded"
                  ()
              }

              ]
