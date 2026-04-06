namespace EffFs.Core.Tests

module Eff =
  open Expecto
  open EffFs.Core
  open System.Threading
  open System.Threading.Tasks

  let tests =
    testList "Core" [
      testTask "Pure resolves" {
        let! value = Pure 5 |> Eff.runTask ()
        Expect.equal value (Exit.Ok 5) "should be equal"
      }

      testTask "Err resolves" {
        let! value = Err "oh no" |> Eff.runTask ()
        Expect.equal value (Exit.Err "oh no") "should be equal"
      }

      testTask "mapErr maps delayed errors" {
        let! value =
          Eff.suspend (fun () -> Err "boom")
          |> Eff.mapErr exn
          |> Eff.runTask ()

        let err: exn = Exit.err value
        Expect.equal err.Message "boom" "should map delayed Err values"
      }

      testTask "mapErr maps bound errors" {
        let! value =
          Pure 1
          |> Eff.bind (fun _ -> Err "boom")
          |> Eff.mapErr exn
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should map Err values after bind"
      }

      testTask "mapErr maps captured exceptions" {
        let! value =
          Eff.tryCatch (fun () -> failwith "boom")
          |> Eff.mapErr (fun e -> exn ($"wrapped: {e.Message}"))
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          err.Message
          "wrapped: boom"
          "should map captured exceptions"
      }

      testTask "Suspend resolves" {
        let! value = Eff.suspend (fun () -> Pure 5) |> Eff.runTask ()
        Expect.equal value (Exit.Ok 5) "should be equal"
      }

      testTask "deep suspend chains stay stack-safe" {
        let depth = 100_000

        let rec loop n =
          if n = 0 then
            Pure 42
          else
            Eff.suspend (fun () -> loop (n - 1))

        let! value = loop depth |> Eff.runTask ()
        Expect.equal value (Exit.Ok 42) "should resolve deep suspend chains"
      }

      testTask "Thunk resolves" {
        let! value = Eff.thunk (fun () -> 5) |> Eff.runTask ()
        Expect.equal value (Exit.Ok 5) "should be equal"
      }

      testTask "Task resolves" {
        let! value = Eff.ofTask (fun () -> task { return 5 }) |> Eff.runTask ()

        Expect.equal value (Exit.Ok 5) "should be equal"
      }

      testTask "Task failure preserves the original exception" {
        let! value =
          Eff.ofTask (fun () -> task {
            failwith "boom"
            return 5
          })
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          (err.GetType())
          typeof<System.Exception>
          "task failure should not be wrapped in AggregateException"

        Expect.equal
          err.Message
          "boom"
          "task failure should preserve the original exception"
      }

      testTask "Task cancellation preserves TaskCanceledException" {
        let cts = new CancellationTokenSource()
        cts.Cancel()

        let! value =
          Eff.ofTask (fun () -> Task.FromCanceled<int>(cts.Token))
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          (err.GetType())
          typeof<TaskCanceledException>
          "task cancellation should surface as TaskCanceledException"
      }

      testTask "ValueTask resolves" {
        let! value =
          Eff.ofValueTask (fun () -> ValueTask<int>(5)) |> Eff.runTask ()

        Expect.equal value (Exit.Ok 5) "should be equal"
      }

      testTask "Async resolves" {
        let! value =
          Eff.ofAsync (fun () -> async { return 5 }) |> Eff.runTask ()

        Expect.equal value (Exit.Ok 5) "should be equal"
      }

      testTask "Async failure preserves the original exception" {
        let! value =
          Eff.ofAsync (fun () -> async { failwith "kaboom" })
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          (err.GetType())
          typeof<System.Exception>
          "async failure should not be wrapped in AggregateException"

        Expect.equal
          err.Message
          "kaboom"
          "async failure should preserve the original exception"
      }

      testTask "Result resolves" {
        let! value = Eff.ofResult (Ok 5) |> Eff.runTask ()
        Expect.equal value (Exit.Ok 5) "should be equal"

        let! value = Eff.ofResult (Error "oh no") |> Eff.runTask ()
        Expect.equal value (Exit.Err "oh no") "should be equal"

        let! value = Eff.ofResultWith exn (Error "oh yes") |> Eff.runTask ()

        let err: exn = Exit.err value
        Expect.equal err.Message ("oh yes") "should be equal"
      }

      testTask "Option resolves" {
        let! value = Eff.ofOption (Some 5) |> Eff.runTask ()
        Expect.equal value (Exit.Ok 5) "should be equal"

        let! value = Eff.ofOption None |> Eff.runTask ()
        let err: exn = Exit.err value

        Expect.equal (err.GetType()) (typeof<Report>) "should be equal"

        let! value =
          Eff.ofOptionWith (fun () -> exn "missing") None |> Eff.runTask ()

        let err: exn = Exit.err value
        Expect.equal err.Message ("missing") "should be equal"
      }

      testTask "ValueOption resolves" {
        let! value = Eff.ofValueOption (ValueSome 5) |> Eff.runTask ()
        Expect.equal value (Exit.Ok 5) "should be equal"

        let! value = Eff.ofValueOption ValueNone |> Eff.runTask ()
        let err: exn = Exit.err value

        Expect.equal (err.GetType()) (typeof<Report>) "should be equal"

        let! value =
          Eff.ofValueOptionWith (fun () -> exn "missing") ValueNone
          |> Eff.runTask ()

        let err: exn = Exit.err value
        Expect.equal err.Message ("missing") "should be equal"
      }

      testTask "exceptions resolve as defects" {
        let! value = Eff.thunk (fun () -> failwith "oh no") |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          err.Message
          "oh no"
          "should capture thrown exceptions as defects"
      }

      testTask "tryCatch captures thrown exceptions as errors" {
        let! value = Eff.tryCatch (fun () -> failwith "oh no") |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          err.Message
          "oh no"
          "should return the thrown exception as Err"
      }

      testTask "tryTask captures thrown exceptions as errors" {
        let! value =
          Eff.tryTask (fun () -> task { failwith "oh no" }) |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          err.Message
          "oh no"
          "should return the thrown exception as Err"
      }

      testTask "tryAsync captures thrown exceptions as errors" {
        let! value =
          Eff.tryAsync (fun () -> async { failwith "oh no" })
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          err.Message
          "oh no"
          "should return the thrown exception as Err"
      }

      testTask "env resolves" {
        let env = {| UserId = 42; Name = "A" |}
        let! value = Eff.ask () |> Eff.runTask env

        Expect.equal value (Exit.Ok env) "should return the current environment"

        let! value =
          Eff.read (fun (e: {| UserId: int; Name: string |}) -> e.UserId)
          |> Eff.bind (fun i -> Pure (i + 1))
          |> Eff.map string
          |> Eff.runTask env

        Expect.equal value (Exit.Ok "43") "should project UserId from env"
      }

      testTask "full usage" {
        let userId =
          Eff.read (fun (e: {| UserId: int; Name: string |}) -> e.UserId)
          |> Eff.bind (fun i -> Pure (i + 1))
          |> Eff.map string
          |> Eff.map int

        let myAsyncVal userId =
          Eff.ofAsync (fun () -> async { return userId })
          |> Eff.bind (fun x -> Pure (x + 1))
          |> Eff.map string
          |> Eff.map int

        let myTaskVal userId =
          Eff.ofTask (fun () -> task { return userId })
          |> Eff.bind (fun x -> Pure (x + 1))
          |> Eff.map string
          |> Eff.map int

        let! value =
          userId
          |> Eff.bind myAsyncVal
          |> Eff.bind myTaskVal
          |> Eff.runTask {| UserId = 0; Name = "Victor" |}

        Expect.equal value (Exit.Ok 3) "should project UserId from env"
      }

      testTask "defer" {
        let mutable number = 0

        do!
          Eff.ofTask (fun () -> task {
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

      testTask "defer runs after tryCatch failure" {
        let mutable cleaned = false

        let! value =
          Eff.tryCatch (fun () -> failwith "boom")
          |> Eff.defer (Eff.thunk (fun () -> cleaned <- true))
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should preserve the captured exception"

        Expect.isTrue
          cleaned
          "defer should run after explicit exception capture"
      }

      testTask "defer runs after tryTask failure" {
        let mutable cleaned = false

        let! value =
          Eff.tryTask (fun () -> task { failwith "boom" })
          |> Eff.defer (Eff.thunk (fun () -> cleaned <- true))
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should preserve the captured exception"

        Expect.isTrue
          cleaned
          "defer should run after explicit task exception capture"
      }

      testTask "defer runs after tryAsync failure" {
        let mutable cleaned = false

        let! value =
          Eff.tryAsync (fun () -> async { failwith "boom" })
          |> Eff.defer (Eff.thunk (fun () -> cleaned <- true))
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should preserve the captured exception"

        Expect.isTrue
          cleaned
          "defer should run after explicit async exception capture"
      }

      testTask "bracket releases after success" {
        let events = ResizeArray<string>()

        let acquire =
          Eff.thunk (fun () ->
            events.Add "acquire"
            42
          )

        let release resource =
          Eff.thunk (fun () -> events.Add $"release {resource}")

        let body resource =
          Eff.thunk (fun () ->
            events.Add $"use {resource}"
            resource + 1
          )

        let! result = Eff.bracket acquire release body |> Eff.runTask ()

        Expect.equal result (Exit.Ok 43) "should return use result"

        Expect.sequenceEqual
          events
          [ "acquire"; "use 42"; "release 42" ]
          "should run in order"
      }

      testTask "bracket releases after failure" {
        let events = ResizeArray<string>()

        let acquire =
          Eff.thunk (fun () ->
            events.Add "acquire"
            42
          )

        let release resource =
          Eff.thunk (fun () -> events.Add $"release {resource}")

        let body resource =
          Eff.thunk (fun () ->
            events.Add $"use {resource}"
            failwith "boom"
          )

        let! result = Eff.bracket acquire release body |> Eff.runTask ()

        let err: exn = Exit.ex result
        Expect.equal err.Message "boom" "should return use error"

        Expect.sequenceEqual
          events
          [ "acquire"; "use 42"; "release 42" ]
          "should still release on failure"
      }

      testTask "bracket releases when use throws before returning an effect" {
        let events = ResizeArray<string>()

        let acquire =
          Eff.thunk (fun () ->
            events.Add "acquire"
            42
          )

        let release resource =
          Eff.thunk (fun () -> events.Add $"release {resource}")

        let body resource =
          events.Add $"use {resource}"
          failwith "boom"

        let! result = Eff.bracket acquire release body |> Eff.runTask ()

        let err: exn = Exit.ex result
        Expect.equal err.Message "boom" "should return use error"

        Expect.sequenceEqual
          events
          [ "acquire"; "use 42"; "release 42" ]
          "should still release when use throws synchronously"
      }

      testTask "bracket release error overrides success" {
        let! result =
          Eff.bracket
            (Pure 42)
            (fun _ -> Err "cleanup failed")
            (fun resource -> Pure(resource + 1))
          |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Err "cleanup failed")
          "cleanup failure should override the successful body result"
      }

      testTask "bracket release error overrides body error" {
        let! result =
          Eff.bracket
            (Pure 42)
            (fun _ -> Err "cleanup failed")
            (fun _ -> Err "body failed")
          |> Eff.runTask ()

        Expect.equal
          result
          (Exit.Err "cleanup failed")
          "cleanup failure should override the body error"
      }

      testTask "deep bind chains stay stack-safe" {
        let depth = 100_000
        let mutable program: Eff<int, unit, unit> = Eff.suspend (fun () -> Pure 0)

        for _ in 1 .. depth do
          program <- program |> Eff.bind (fun value -> Pure(value + 1))

        let! value = program |> Eff.runTask ()
        Expect.equal value (Exit.Ok depth) "should resolve deep bind chains"
      }

    ]
