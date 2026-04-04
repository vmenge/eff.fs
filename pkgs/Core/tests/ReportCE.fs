namespace EffFs.Core.Tests

module ReportCE =
  open Expecto
  open System.Threading.Tasks
  open EffFs.Core

  let tests =
    testList "ReportCE" [
      testTask "pure return can be annotated as exn error" {
        let program () : Eff<int, exn, unit> = effr { return 1 }

        let! value = program () |> Eff.runTask ()

        Expect.equal
          value
          (Exit.Ok 1)
          "pure return should stay usable as exn effect"
      }

      testTask "let! normalizes Eff errors to Report" {
        let! value =
          effr {
            let! x = Eff.err "boom"
            return x
          }
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          (err.GetType())
          typeof<Report>
          "non-exn errors should be wrapped in Report"

        Expect.equal
          err.Message
          "boom"
          "report message should come from the original error"

        match err with
        | ReportAs(wrapped: string) ->
          Expect.equal
            wrapped
            "boom"
            "report should preserve the original error payload"
        | _ -> failtest "expected Report carrying the original string"
      }

      testTask "return! normalizes Eff errors to Report" {
        let! value = effr { return! Eff.err "boom" } |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          (err.GetType())
          typeof<Report>
          "return! should normalize Eff errors"

        match err with
        | ReportAs(wrapped: string) ->
          Expect.equal
            wrapped
            "boom"
            "report should preserve the original error payload"
        | _ -> failtest "expected Report carrying the original string"
      }

      testTask "result errors normalize to Report" {
        let! value =
          effr {
            let! _ = Error "boom"
            return 1
          }
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          (err.GetType())
          typeof<Report>
          "result errors should normalize to Report"

        match err with
        | ReportAs(wrapped: string) ->
          Expect.equal
            wrapped
            "boom"
            "report should preserve the original result error"
        | _ -> failtest "expected Report carrying the original string"
      }

      testTask "task result errors normalize to Report" {
        let taskResult () : Task<Result<int, string>> = task {
          return Error "boom"
        }

        let! value =
          effr {
            let! _ = taskResult ()
            return 1
          }
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          (err.GetType())
          typeof<Report>
          "task result errors should normalize to Report"

        match err with
        | ReportAs(wrapped: string) ->
          Expect.equal
            wrapped
            "boom"
            "report should preserve the original task result error"
        | _ -> failtest "expected Report carrying the original string"
      }

      testTask "mixed let! chain normalizes later Eff errors to Report" {
        let! value =
          effr {
            let! x = Ok 1
            let! y = if x = 1 then Eff.err "boom" else Eff.value 2
            return y
          }
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          (err.GetType())
          typeof<Report>
          "later Eff errors should still normalize to Report"

        match err with
        | ReportAs(wrapped: string) ->
          Expect.equal
            wrapped
            "boom"
            "report should preserve the later Eff error payload"
        | _ -> failtest "expected Report carrying the original string"
      }

      testTask "mixed successful chain stays in the report CE" {
        let taskResult () : Task<Result<int, string>> = task { return Ok 2 }

        let! value =
          effr {
            let! x = Ok 1
            let! y = taskResult ()
            let! z = Eff.value 3
            return x + y + z
          }
          |> Eff.runTask ()

        Expect.equal value (Exit.Ok 6) "mixed successful sources should compose"
      }

      testTask "option none stays a single Report" {
        let! value =
          effr {
            let! _ = None
            return 1
          }
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          (err.GetType())
          typeof<Report>
          "option none should surface as Report"

        match err with
        | ReportAs(wrapped: Option<int>) ->
          Expect.equal
            wrapped
            None
            "report should preserve the original option payload"
        | _ -> failtest "expected Report carrying None"
      }

      testTask
        "existing exceptions are wrapped once and preserved as inner exceptions" {
        let boom = exn "boom"

        let! value =
          effr {
            let! _ = Eff.err boom
            return 1
          }
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.equal
          (err.GetType())
          typeof<Report>
          "plain exceptions should normalize to Report"

        Expect.equal
          err.Message
          "boom"
          "report message should match the original exception"

        Expect.isTrue
          (obj.ReferenceEquals(err.InnerException, boom))
          "original exception should be preserved as InnerException"
      }

      testTask "existing reports are not rewrapped" {
        let boom = Report.make "boom"

        let! value =
          effr {
            let! _ = Eff.err boom
            return 1
          }
          |> Eff.runTask ()

        let err: exn = Exit.err value

        Expect.isTrue
          (obj.ReferenceEquals(err, boom))
          "existing reports should flow through unchanged"
      }
    ]
