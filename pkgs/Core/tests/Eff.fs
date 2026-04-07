namespace EffSharp.Core.Tests

module Eff =
  open Expecto
  open EffSharp.Core
  open System.Diagnostics
  open System.IO
  open System.Threading
  open System.Threading.Tasks

  let private runFsiScript (script: string) = task {
    let tempDir =
      Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())

    Directory.CreateDirectory(tempDir) |> ignore

    let scriptPath = Path.Combine(tempDir, "script.fsx")
    do! File.WriteAllTextAsync(scriptPath, script)

    let startInfo = ProcessStartInfo("dotnet", $"fsi {scriptPath}")
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.UseShellExecute <- false

    use proc = new Process(StartInfo = startInfo)

    if not (proc.Start()) then
      failwith "failed to start dotnet fsi"

    do! proc.WaitForExitAsync()

    let stdout = proc.StandardOutput.ReadToEnd()
    let stderr = proc.StandardError.ReadToEnd()

    try
      Directory.Delete(tempDir, true)
    with _ ->
      ()

    return proc.ExitCode, stdout + stderr
  }

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
        Expect.equal err.Message "boom" "should map delayed Eff.Err values"
      }

      testTask "mapErr maps bound errors" {
        let! value =
          Pure 1
          |> Eff.bind (fun _ -> Err "boom")
          |> Eff.mapErr exn
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should map Eff.Err values after bind"
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

      testTask "map on Pure captures mapper exceptions as Exit.Exn" {
        let! value =
          Pure 1
          |> Eff.map (fun _ -> failwith "boom")
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          err.Message
          "boom"
          "Pure |> map should surface mapper exceptions as Exit.Exn"
      }

      testTask "bind on Pure captures continuation exceptions as Exit.Exn" {
        let! value =
          Pure 1
          |> Eff.bind (fun _ -> failwith "boom")
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          err.Message
          "boom"
          "Pure |> bind should surface continuation exceptions as Exit.Exn"
      }

      testTask "mapErr on Err captures mapper exceptions as Exit.Exn" {
        let! value =
          Err "boom"
          |> Eff.mapErr (fun _ -> failwith "wrapped boom")
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          err.Message
          "wrapped boom"
          "Err |> mapErr should surface mapper exceptions as Exit.Exn"
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
          "should return the thrown exception as Eff.Err"
      }

      testTask "tryTask captures thrown exceptions as errors" {
        let! value =
          Eff.tryTask (fun () -> task { failwith "oh no" }) |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          err.Message
          "oh no"
          "should return the thrown exception as Eff.Err"
      }

      testTask "tryAsync captures thrown exceptions as errors" {
        let! value =
          Eff.tryAsync (fun () -> async { failwith "oh no" })
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          err.Message
          "oh no"
          "should return the thrown exception as Eff.Err"
      }

      testTask "env resolves" {
        let env = {| UserId = 42; Name = "A" |}
        let! value = Eff.ask () |> Eff.runTask env

        Expect.equal value (Exit.Ok env) "should return the current environment"

        let! value =
          Eff.read (fun (e: {| UserId: int; Name: string |}) -> e.UserId)
          |> Eff.bind (fun i -> Pure(i + 1))
          |> Eff.map string
          |> Eff.runTask env

        Expect.equal value (Exit.Ok "43") "should project UserId from env"
      }

      testTask "flatten unwraps nested pure" {
        let! value =
          Pure(Pure 42)
          |> Eff.flatten
          |> Eff.runTask ()

        Expect.equal value (Exit.Ok 42) "should unwrap the inner effect"
      }

      testTask "flatten preserves outer error" {
        let! value =
          Err "boom"
          |> Eff.flatten
          |> Eff.runTask ()

        Expect.equal value (Exit.Err "boom") "outer error should short-circuit"
      }

      testTask "flatten preserves inner error" {
        let! value =
          Pure(Err "boom")
          |> Eff.flatten
          |> Eff.runTask ()

        Expect.equal value (Exit.Err "boom") "inner error should be preserved"
      }

      testTask "flatten runs inner effect in the same ambient environment" {
        let nested : Eff<Eff<int, unit, {| UserId: int |}>, unit, {| UserId: int |}> =
          Pure(Eff.read (fun (env: {| UserId: int |}) -> env.UserId))

        let! value =
          nested
          |> Eff.flatten
          |> Eff.runTask {| UserId = 42 |}

        Expect.equal value (Exit.Ok 42) "inner effect should see the same env"
      }

      testTask "provide supplies a concrete environment" {
        let! value =
          Eff.read id
          |> Eff.provide 42
          |> Eff.runTask ()

        Expect.equal value (Exit.Ok 42) "should use the provided environment"
      }

      testTask "provideFrom projects a larger environment" {
        let inner : Eff<int, unit, {| UserId: int |}> =
          Eff.read (fun (env: {| UserId: int |}) -> env.UserId + 1)

        let! value =
          inner
          |> Eff.provideFrom (fun (env: {| UserId: int; Name: string |}) ->
            {| UserId = env.UserId |})
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        Expect.equal value (Exit.Ok 42) "should project the outer environment"
      }

      testTask "provideFrom only scopes the subtree" {
        let program : Eff<string * int * string, unit, {| UserId: int; Name: string |}> =
          eff {
            let! before =
              Eff.read (fun (env: {| UserId: int; Name: string |}) -> env.Name)

            let! userId =
              (Eff.read (fun (env: {| UserId: int |}) -> env.UserId)
               |> Eff.provideFrom (fun (env: {| UserId: int; Name: string |}) ->
                 {| UserId = env.UserId |}))

            let! after =
              Eff.read (fun (env: {| UserId: int; Name: string |}) -> env.Name)

            return before, userId, after
          }

        let! value = program |> Eff.runTask {| UserId = 42; Name = "Victor" |}

        Expect.equal
          value
          (Exit.Ok("Victor", 42, "Victor"))
          "outer environment should remain visible before and after the subtree"
      }

      testTask "provideFrom works across task suspension" {
        let inner : Eff<int, unit, {| UserId: int |}> =
          eff {
            let! baseValue = Eff.ofTask (fun () -> task { return 1 })
            let! userId = Eff.read (fun (env: {| UserId: int |}) -> env.UserId)
            return baseValue + userId
          }

        let! value =
          inner
          |> Eff.provideFrom (fun (env: {| UserId: int; Name: string |}) ->
            {| UserId = env.UserId |})
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        Expect.equal value (Exit.Ok 42) "projected env should survive suspension"
      }

      testTask "provideFrom preserves inner managed errors" {
        let inner : Eff<int, string, {| UserId: int |}> = Err "boom"

        let! value =
          inner
          |> Eff.provideFrom (fun (env: {| UserId: int; Name: string |}) ->
            {| UserId = env.UserId |})
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        Expect.equal value (Exit.Err "boom") "inner managed errors should flow out"
      }

      testTask "provideFrom preserves inner defects" {
        let inner : Eff<int, unit, {| UserId: int |}> =
          Eff.thunk (fun () -> failwith "boom")

        let! value =
          inner
          |> Eff.provideFrom (fun (env: {| UserId: int; Name: string |}) ->
            {| UserId = env.UserId |})
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        let err: exn = Exit.ex value
        Expect.equal err.Message "boom" "inner defects should flow out"
      }

      testTask "provideFrom projection defects resolve as Exit.Exn" {
        let! value =
          (Pure 1 : Eff<int, string, {| UserId: int; Name: string |}>)
          |> Eff.provideFrom (fun _ -> failwith "boom")
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        let err: exn = Exit.ex value
        Expect.equal err.Message "boom" "projection failures should stay in Exit.Exn"
      }

      testTask "provideFrom projection defects are catchable" {
        let! value =
          (Pure 1 : Eff<int, string, {| UserId: int; Name: string |}>)
          |> Eff.provideFrom (fun _ -> failwith "boom")
          |> Eff.catch (fun _ -> Pure 2)
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        Expect.equal
          value
          (Exit.Ok 2)
          "outer catch should recover from projection defects"
      }

      testTask "provideFrom runs inner defer" {
        let mutable cleaned = false

        let inner : Eff<int, unit, {| UserId: int |}> =
          Eff.read (fun (env: {| UserId: int |}) -> env.UserId)
          |> Eff.ensure (Eff.thunk (fun () -> cleaned <- true))

        let! value =
          inner
          |> Eff.provideFrom (fun (env: {| UserId: int; Name: string |}) ->
            {| UserId = env.UserId |})
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        Expect.equal value (Exit.Ok 41) "inner value should be preserved"
        Expect.isTrue cleaned "inner defer should run"
      }

      testTask "provideFrom runs inner bracket release" {
        let events = ResizeArray<string>()

        let inner : Eff<int, unit, {| UserId: int |}> =
          Eff.bracket
            (Eff.read (fun (env: {| UserId: int |}) ->
              events.Add $"acquire {env.UserId}"
              env.UserId))
            (fun resource ->
              Eff.thunk (fun () -> events.Add $"release {resource}"))
            (fun resource ->
              Eff.thunk (fun () ->
                events.Add $"use {resource}"
                resource + 1))

        let! value =
          inner
          |> Eff.provideFrom (fun (env: {| UserId: int; Name: string |}) ->
            {| UserId = env.UserId |})
          |> Eff.runTask {| UserId = 41; Name = "Victor" |}

        Expect.equal value (Exit.Ok 42) "inner bracket result should be preserved"

        Expect.sequenceEqual
          events
          [ "acquire 41"; "use 41"; "release 41" ]
          "inner bracket should still release"
      }

      testTask "full usage" {
        let userId =
          Eff.read (fun (e: {| UserId: int; Name: string |}) -> e.UserId)
          |> Eff.bind (fun i -> Pure(i + 1))
          |> Eff.map string
          |> Eff.map int

        let myAsyncVal userId =
          Eff.ofAsync (fun () -> async { return userId })
          |> Eff.bind (fun x -> Pure(x + 1))
          |> Eff.map string
          |> Eff.map int

        let myTaskVal userId =
          Eff.ofTask (fun () -> task { return userId })
          |> Eff.bind (fun x -> Pure(x + 1))
          |> Eff.map string
          |> Eff.map int

        let! value =
          userId
          |> Eff.bind myAsyncVal
          |> Eff.bind myTaskVal
          |> Eff.runTask {| UserId = 0; Name = "Victor" |}

        Expect.equal value (Exit.Ok 3) "should project UserId from env"
      }

      testTask "ensure" {
        let mutable number = 0

        do!
          Eff.ofTask (fun () -> task {
            failwith "oh no!"
            return 5
          })
          |> Eff.ensure (Eff.thunk (fun () -> number <- 1))
          |> Eff.runTask ()

        Expect.equal number 1 "ensure should have run"

        do!
          Eff.ofTask (fun () -> task { return Error(exn "oh no") })
          |> Eff.bind Eff.ofResult
          |> Eff.ensure (Eff.thunk (fun () -> number <- 2))
          |> Eff.runTask ()

        Expect.equal number 2 "ensure should have run"

        do!
          Eff.ofTask (fun () -> task { return 5 })
          |> Eff.ensure (Eff.thunk (fun () -> number <- 3))
          |> Eff.runTask ()

        Expect.equal number 3 "ensure should have run"
      }

      testTask "ensure runs after tryCatch failure" {
        let mutable cleaned = false

        let! value =
          Eff.tryCatch (fun () -> failwith "boom")
          |> Eff.ensure (Eff.thunk (fun () -> cleaned <- true))
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should preserve the captured exception"

        Expect.isTrue
          cleaned
          "ensure should run after explicit exception capture"
      }

      testTask "ensure runs after tryTask failure" {
        let mutable cleaned = false

        let! value =
          Eff.tryTask (fun () -> task { failwith "boom" })
          |> Eff.ensure (Eff.thunk (fun () -> cleaned <- true))
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should preserve the captured exception"

        Expect.isTrue
          cleaned
          "ensure should run after explicit task exception capture"
      }

      testTask "ensure runs after tryAsync failure" {
        let mutable cleaned = false

        let! value =
          Eff.tryAsync (fun () -> async { failwith "boom" })
          |> Eff.ensure (Eff.thunk (fun () -> cleaned <- true))
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal err.Message "boom" "should preserve the captured exception"

        Expect.isTrue
          cleaned
          "ensure should run after explicit async exception capture"
      }

      testTask "ensure finalizes the chain it wraps before outer continuation runs" {
        let events = ResizeArray<string>()

        let! value =
          (Pure 1 : Eff<int, unit, unit>)
          |> Eff.bind (fun x ->
            Eff.thunk (fun () ->
              events.Add $"body {x}"
              x + 1)
          )
          |> Eff.ensure (Eff.thunk (fun () -> events.Add "cleanup"))
          |> Eff.bind (fun x ->
            Eff.thunk (fun () ->
              events.Add $"next {x}"
              x + 1)
          )
          |> Eff.runTask ()

        Expect.equal value (Exit.Ok 3) "outer continuation should still receive the finalized value"

        Expect.sequenceEqual
          events
          [ "body 1"; "cleanup"; "next 2" ]
          "ensure should run after the wrapped chain terminates and before outer continuation resumes"
      }

      testTask "ensure cleanup failure prevents outer continuation" {
        let events = ResizeArray<string>()

        let! value =
          (Pure 1 : Eff<int, string, unit>)
          |> Eff.bind (fun x ->
            Eff.thunk (fun () ->
              events.Add $"body {x}"
              x + 1)
          )
          |> Eff.ensure (Err "cleanup failed")
          |> Eff.bind (fun x ->
            Eff.thunk (fun () ->
              events.Add $"next {x}"
              x + 1)
          )
          |> Eff.runTask ()

        Expect.equal
          value
          (Exit.Err "cleanup failed")
          "cleanup failure should override the wrapped chain before outer continuation runs"

        Expect.sequenceEqual
          events
          [ "body 1" ]
          "outer continuation should not run once ensure cleanup fails"
      }

      testTask "tapErr runs side effect and preserves the original error" {
        let mutable seen = ""

        let! value =
          Err "boom"
          |> Eff.tapErr (fun err ->
            Eff.thunk (fun () -> seen <- $"logged: {err}")
          )
          |> Eff.runTask ()

        Expect.equal value (Exit.Err "boom") "should preserve the original error"
        Expect.equal seen "logged: boom" "should run the tapErr side effect"
      }

      testTask "tapErr does not run on success" {
        let mutable called = false

        let! value =
          Pure 42
          |> Eff.tapErr (fun _ ->
            Eff.thunk (fun () -> called <- true)
          )
          |> Eff.runTask ()

        Expect.equal value (Exit.Ok 42) "should preserve the successful result"
        Expect.isFalse called "tapErr should not run on success"
      }

      testTask "tapErr handler error overrides the original error" {
        let! value =
          Err "boom"
          |> Eff.tapErr (fun _ -> Err "handler failed")
          |> Eff.runTask ()

        Expect.equal
          value
          (Exit.Err "handler failed")
          "handler errors should override the original error"
      }

      testTask "tapErr handler defect overrides the original error" {
        let! value =
          Err "boom"
          |> Eff.tapErr (fun _ -> Eff.thunk (fun () -> failwith "tap failed"))
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          err.Message
          "tap failed"
          "handler defects should override the original error"
      }

      testTask "catch recovers from defects" {
        let! value =
          Eff.thunk (fun () -> failwith "boom")
          |> Eff.catch (fun ex -> Pure ex.Message)
          |> Eff.runTask ()

        Expect.equal value (Exit.Ok "boom") "should recover from defects"
      }

      testTask "catch does not handle managed errors" {
        let! value =
          Err "boom"
          |> Eff.catch (fun _ -> Pure "recovered")
          |> Eff.runTask ()

        Expect.equal value (Exit.Err "boom") "should preserve managed errors"
      }

      testTask "catch handler defect replaces the original defect" {
        let! value =
          Eff.thunk (fun () -> failwith "boom")
          |> Eff.catch (fun _ -> Eff.thunk (fun () -> failwith "replacement"))
          |> Eff.runTask ()

        let err: exn = Exit.ex value
        Expect.equal err.Message "replacement" "handler defect should override"
      }

      testTask "tapExn runs side effect and preserves the original defect" {
        let mutable seen = ""

        let! value =
          Eff.thunk (fun () -> failwith "boom")
          |> Eff.tapExn (fun ex ->
            Eff.thunk (fun () -> seen <- $"logged: {ex.Message}")
          )
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal err.Message "boom" "should preserve the original defect"
        Expect.equal seen "logged: boom" "should run the tapExn side effect"
      }

      testTask "tapExn does not run on managed errors" {
        let mutable called = false

        let! value =
          Err "boom"
          |> Eff.tapExn (fun _ ->
            Eff.thunk (fun () -> called <- true)
          )
          |> Eff.runTask ()

        Expect.equal value (Exit.Err "boom") "should preserve managed errors"
        Expect.isFalse called "tapExn should not run on managed errors"
      }

      testTask "tapExn handler error overrides the original defect" {
        let! value =
          Eff.thunk (fun () -> failwith "boom")
          |> Eff.tapExn (fun _ -> Err "handler failed")
          |> Eff.runTask ()

        Expect.equal
          value
          (Exit.Err "handler failed")
          "handler errors should override the original defect"
      }

      testTask "tapExn handler defect overrides the original defect" {
        let! value =
          Eff.thunk (fun () -> failwith "boom")
          |> Eff.tapExn (fun _ -> Eff.thunk (fun () -> failwith "tap failed"))
          |> Eff.runTask ()

        let err: exn = Exit.ex value

        Expect.equal
          err.Message
          "tap failed"
          "handler defects should override the original defect"
      }

      testTask "defer runs before catch handles a defect" {
        let mutable cleaned = false

        let! value =
          Eff.thunk (fun () -> failwith "boom")
          |> Eff.ensure (Eff.thunk (fun () -> cleaned <- true))
          |> Eff.catch (fun ex -> Pure ex.Message)
          |> Eff.runTask ()

        Expect.equal value (Exit.Ok "boom") "should recover from the defect"
        Expect.isTrue cleaned "defer should run before catch handles the defect"
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
