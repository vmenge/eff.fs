namespace EffFs.Core.Tests

module Eff =
    open Expecto
    open EffFs.Core
    open System.Threading.Tasks

    let tests =
        testList
            "Core"
            [ testTask "Pure resolves" {
                  let! value = Eff.value 5 |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"
              }

              testTask "Err resolves" {
                  let! value = Eff.err (exn "oh no") |> Eff.runTask ()
                  let err: exn = Result.error value
                  Expect.equal err.Message ("oh no") "should be equal"
              }

              testTask "Delay resolves" {
                  let! value = Eff.delay (fun () -> Eff.value 5) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"
              }

              testTask "Thunk resolves" {
                  let! value = Eff.thunk (fun () -> 5) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"
              }

              testTask "Task resolves" {
                  let! value = Eff.ofTask (fun () -> task { return 5 }) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"
              }

              testTask "ValueTask resolves" {
                  let! value = Eff.ofValueTask (fun () -> ValueTask<int>(5)) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"
              }

              testTask "Async resolves" {
                  let! value = Eff.ofAsync (fun () -> async { return 5 }) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"
              }

              testTask "Result resolves" {
                  let! value = Eff.ofResult (Ok 5) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"

                  let! value = Eff.ofResult (Error <| exn "oh no") |> Eff.runTask ()
                  let err: exn = Result.error value
                  Expect.equal err.Message ("oh no") "should be equal"

                  let! value = Eff.ofResultWith exn (Error "oh yes") |> Eff.runTask ()
                  let err: exn = Result.error value
                  Expect.equal err.Message ("oh yes") "should be equal"
              }

              testTask "Option resolves" {
                  let! value = Eff.ofOption (Some 5) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"

                  let! value = Eff.ofOption None |> Eff.runTask ()
                  let err: exn = Result.error value
                  Expect.equal (err.GetType()) (typeof<ValueIsNone>) "should be equal"

                  let! value = Eff.ofOptionWith (fun () -> exn "missing") None |> Eff.runTask ()
                  let err: exn = Result.error value
                  Expect.equal err.Message ("missing") "should be equal"
              }

              testTask "ValueOption resolves" {
                  let! value = Eff.ofValueOption (ValueSome 5) |> Eff.runTask ()
                  Expect.equal value (Ok 5) "should be equal"

                  let! value = Eff.ofValueOption ValueNone |> Eff.runTask ()
                  let err: exn = Result.error value
                  Expect.equal (err.GetType()) (typeof<ValueIsNone>) "should be equal"

                  let! value = Eff.ofValueOptionWith (fun () -> exn "missing") ValueNone |> Eff.runTask ()
                  let err: exn = Result.error value
                  Expect.equal err.Message ("missing") "should be equal"
              }

              testTask "env resolves" {
                  let env = {| UserId = 42; Name = "A" |}
                  let! value = Eff.ask () |> Eff.runTask env
                  Expect.equal value (Ok env) "should return the current environment"

                  let! value =
                      Eff.read (fun (e: {| UserId: int; Name: string |}) -> e.UserId)
                      |> Eff.bind (fun i -> Eff.value (i + 1))
                      |> Eff.map string
                      |> Eff.runTask env

                  Expect.equal value (Ok "43") "should project UserId from env"
              }

              testTask "full usage" {
                  let userId =
                      Eff.read (fun (e: {| UserId: int; Name: string |}) -> e.UserId)
                      |> Eff.bind (fun i -> Eff.value (i + 1))
                      |> Eff.map string
                      |> Eff.map int

                  let myAsyncVal userId =
                      Eff.ofAsync (fun () -> async { return userId })
                      |> Eff.bind (fun x -> Eff.value (x + 1))
                      |> Eff.map string
                      |> Eff.map int

                  let myTaskVal userId =
                      Eff.ofTask (fun () -> task { return userId })
                      |> Eff.bind (fun x -> Eff.value (x + 1))
                      |> Eff.map string
                      |> Eff.map int

                  let! value =
                      userId
                      |> Eff.bind myAsyncVal
                      |> Eff.bind myTaskVal
                      |> Eff.runTask {| UserId = 0; Name = "Victor" |}

                  Expect.equal value (Ok 3) "should project UserId from env"
              }

              testTask "defer" {
                  let mutable number = 0

                  do!
                      Eff.ofTask (fun () ->
                          task {
                              failwith "oh no!"
                              return 5
                          })
                      |> Eff.defer (Eff.thunk (fun () -> number <- 1))
                      |> Eff.runTask ()

                  Expect.equal number 1 "defer should have run"

                  do!
                      Eff.ofTask (fun () -> task { return Error(exn "oh no") })
                      |> Eff.bind Eff.ofResult
                      |> Eff.defer (Eff.thunk (fun () -> number <- 2))
                      |> Eff.runTask ()

                  Expect.equal number 2 "defer should have run"

                  do!
                      Eff.ofTask (fun () -> task { return 5 })
                      |> Eff.defer (Eff.thunk (fun () -> number <- 3))
                      |> Eff.runTask ()

                  Expect.equal number 3 "defer should have run"
              }

              testTask "bracket releases after success" {
                  let events = ResizeArray<string>()

                  let acquire =
                      Eff.thunk (fun () ->
                          events.Add "acquire"
                          42)

                  let release resource =
                      Eff.thunk (fun () -> events.Add $"release {resource}")

                  let body resource =
                      Eff.thunk (fun () ->
                          events.Add $"use {resource}"
                          resource + 1)

                  let! result = Eff.bracket acquire release body |> Eff.runTask ()

                  Expect.equal result (Ok 43) "should return use result"
                  Expect.sequenceEqual events [ "acquire"; "use 42"; "release 42" ] "should run in order"
              }

              testTask "bracket releases after failure" {
                  let events = ResizeArray<string>()

                  let acquire =
                      Eff.thunk (fun () ->
                          events.Add "acquire"
                          42)

                  let release resource =
                      Eff.thunk (fun () -> events.Add $"release {resource}")

                  let body resource =
                      Eff.thunk (fun () ->
                          events.Add $"use {resource}"
                          failwith "boom")

                  let! result = Eff.bracket acquire release body |> Eff.runTask ()

                  let err: exn = Result.error result
                  Expect.equal err.Message "boom" "should return use error"
                  Expect.sequenceEqual events [ "acquire"; "use 42"; "release 42" ] "should still release on failure"
              }

              ]
