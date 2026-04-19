namespace EffSharp.Std.Tests

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open EffSharp.Core
open EffSharp.Std

module Stdio =
  type private ProbeStream(initial: byte array) =
    inherit System.IO.Stream()

    let inner = new System.IO.MemoryStream()

    do
      if initial.Length > 0 then
        inner.Write(initial, 0, initial.Length)
        inner.Position <- 0L

    let mutable flushCount = 0
    let mutable activeWrites = 0
    let mutable sawOverlap = false

    member _.FlushCount = flushCount
    member _.SawOverlap = sawOverlap

    override _.CanRead = inner.CanRead
    override _.CanSeek = inner.CanSeek
    override _.CanWrite = inner.CanWrite
    override _.Length = inner.Length

    override _.Position
      with get () = inner.Position
      and set value = inner.Position <- value

    override _.Flush() = inner.Flush()

    override _.FlushAsync(cancellationToken: CancellationToken) =
      task { flushCount <- flushCount + 1 } :> Task

    override _.Read(buffer: byte array, offset: int, count: int) =
      inner.Read(buffer, offset, count)

    override _.Seek(offset: int64, origin: System.IO.SeekOrigin) =
      inner.Seek(offset, origin)

    override _.SetLength(value: int64) = inner.SetLength(value)

    override _.Write(buffer: byte array, offset: int, count: int) =
      inner.Write(buffer, offset, count)

    override _.WriteAsync
      (
        buffer: byte array,
        offset: int,
        count: int,
        cancellationToken: CancellationToken
      ) =
      task {
        let concurrent = Interlocked.Increment(&activeWrites)

        if concurrent > 1 then
          sawOverlap <- true

        try
          do! Task.Delay(10, cancellationToken)
          do! inner.WriteAsync(buffer, offset, count, cancellationToken)
        finally
          Interlocked.Decrement(&activeWrites) |> ignore
      }
      :> Task

    override _.Dispose(disposing: bool) =
      if disposing then
        inner.Dispose()

      base.Dispose(disposing)

  let private utf8 (text: string) = Encoding.UTF8.GetBytes(text)
  let private decodeUtf8 (bytes: byte array) = Encoding.UTF8.GetString(bytes)

  let private create stdin stdout stderr =
    StdioProvider(stdin, stdout, stderr, Encoding.UTF8, Encoding.UTF8, "\n")
    :> EffSharp.Std.Stdio

  let private run eff = task {
    let! exit = Eff.runTask () eff
    return Exit.ok exit
  }

  let tests =
    testList "Stdio" [
      testCase
        "Provider returns a singleton"
        (fun () ->
          let a = EffSharp.Std.Stdio.Provider()
          let b = EffSharp.Std.Stdio.Provider()

          Expect.isTrue
            (obj.ReferenceEquals(a, b))
            "provider should be singleton"
        )

      testTask "eflush flushes stderr, not stdout" {
        use stdin = new MemoryStream()
        use stdout = new ProbeStream([||])
        use stderr = new ProbeStream([||])
        let stdio = create stdin stdout stderr

        do! stdio.eflush () |> run

        Expect.equal stdout.FlushCount 0 "stdout should not be flushed"
        Expect.equal stderr.FlushCount 1 "stderr should be flushed"
      }

      testTask "stdout serializes mixed text and byte writes" {
        use stdin = new MemoryStream()
        use stdout = new ProbeStream([||])
        use stderr = new ProbeStream([||])
        let stdio = create stdin stdout stderr

        do!
          Task.WhenAll(
            [|
              run (stdio.print "A") :> Task
              run (stdio.write [| byte 'B' |]) :> Task
              run (stdio.println "C") :> Task
            |]
          )

        Expect.isFalse stdout.SawOverlap "stdout writes should not overlap"
      }

      testTask "stderr serializes mixed text and byte writes" {
        use stdin = new MemoryStream()
        use stdout = new ProbeStream([||])
        use stderr = new ProbeStream([||])
        let stdio = create stdin stdout stderr

        do!
          Task.WhenAll(
            [|
              run (stdio.eprint "A") :> Task
              run (stdio.ewrite [| byte 'B' |]) :> Task
              run (stdio.eprintln "C") :> Task
            |]
          )

        Expect.isFalse stderr.SawOverlap "stderr writes should not overlap"
      }

      testTask "stdin keeps one cursor across read then readln then read" {
        use stdin = new MemoryStream(utf8 "abc\ndef")
        use stdout = new ProbeStream([||])
        use stderr = new ProbeStream([||])
        let stdio = create stdin stdout stderr

        let! first = stdio.read 2 |> run
        let! line = stdio.readln () |> run
        let! rest = stdio.read 3 |> run

        Expect.equal
          (decodeUtf8 first)
          "ab"
          "first read should consume first bytes"

        Expect.equal
          line
          (Some "c")
          "readln should continue from the same cursor"

        Expect.equal
          (decodeUtf8 rest)
          "def"
          "remaining bytes should still be available"
      }

      testTask "stdin keeps one cursor across readln then read" {
        use stdin = new MemoryStream(utf8 "abc\ndef")
        use stdout = new ProbeStream([||])
        use stderr = new ProbeStream([||])
        let stdio = create stdin stdout stderr

        let! line = stdio.readln () |> run
        let! rest = stdio.read 3 |> run

        Expect.equal line (Some "abc") "readln should read the line"

        Expect.equal
          (decodeUtf8 rest)
          "def"
          "read should continue after newline"
      }

      testTask "readln handles CRLF" {
        use stdin = new MemoryStream(utf8 "abc\r\ndef")
        use stdout = new ProbeStream([||])
        use stderr = new ProbeStream([||])
        let stdio = create stdin stdout stderr

        let! line = stdio.readln () |> run
        let! rest = stdio.read 3 |> run

        Expect.equal line (Some "abc") "CRLF should terminate the line"
        Expect.equal (decodeUtf8 rest) "def" "cursor should advance past CRLF"
      }
    ]
