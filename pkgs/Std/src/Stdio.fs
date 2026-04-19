namespace EffSharp.Std

open EffSharp.Core
open EffSharp.Gen
open System
open System.Buffers
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks

[<Effect(Mode.Wrap)>]
type Stdio =
  abstract print: string -> Eff<unit, 'e, 'env>
  abstract println: string -> Eff<unit, 'e, 'env>
  abstract eprint: string -> Eff<unit, 'e, 'env>
  abstract eprintln: string -> Eff<unit, 'e, 'env>

  abstract readln: unit -> Eff<string option, 'e, 'env>
  abstract read: int -> Eff<byte array, 'e, 'env>
  abstract readToString: unit -> Eff<string, 'e, 'env>
  abstract readToEnd: unit -> Eff<byte array, 'e, 'env>

  abstract write: byte array -> Eff<unit, 'e, 'env>
  abstract ewrite: byte array -> Eff<unit, 'e, 'env>

  abstract flush: unit -> Eff<unit, 'e, 'env>
  abstract eflush: unit -> Eff<unit, 'e, 'env>

  abstract isTerminal: unit -> bool
  abstract isOutTerminal: unit -> bool
  abstract isErrTerminal: unit -> bool

type StdioProvider
  internal
  (
    stdin: Stream,
    stdout: Stream,
    stderr: Stream,
    inputEncoding: Encoding,
    outputEncoding: Encoding,
    newLine: string
  ) =
  static let provider: Stdio =
    StdioProvider(
      Console.OpenStandardInput(),
      Console.OpenStandardOutput(),
      Console.OpenStandardError(),
      Console.InputEncoding,
      Console.OutputEncoding,
      Console.Out.NewLine
    )
    :> Stdio

  let newLineBytes = outputEncoding.GetBytes(newLine)

  let stdinGate = new SemaphoreSlim(1, 1)
  let stdoutGate = new SemaphoreSlim(1, 1)
  let stderrGate = new SemaphoreSlim(1, 1)

  let mutable stdinBuffer = Array.zeroCreate<byte> 4096
  let mutable stdinOffset = 0
  let mutable stdinCount = 0
  let mutable stdinEof = false

  let withGate (gate: SemaphoreSlim) (op: unit -> Task<'t>) : Task<'t> = task {
    do! gate.WaitAsync()

    try
      return! op ()
    finally
      gate.Release() |> ignore
  }

  let compactStdinBuffer () =
    if stdinOffset > 0 && stdinCount > 0 then
      Buffer.BlockCopy(stdinBuffer, stdinOffset, stdinBuffer, 0, stdinCount)

    stdinOffset <- 0

  let ensureStdinCapacity minFree =
    compactStdinBuffer ()

    if stdinBuffer.Length - stdinCount < minFree then
      let targetLength = max (stdinBuffer.Length * 2) (stdinCount + minFree)

      let next = Array.zeroCreate<byte> targetLength

      if stdinCount > 0 then
        Buffer.BlockCopy(stdinBuffer, 0, next, 0, stdinCount)

      stdinBuffer <- next

  let consumeStdin count =
    stdinOffset <- stdinOffset + count
    stdinCount <- stdinCount - count

    if stdinCount = 0 then
      stdinOffset <- 0

  let refillStdinBuffer () : Task<bool> = task {
    if stdinEof then
      return false
    else
      ensureStdinCapacity 1024

      let writeOffset = stdinOffset + stdinCount

      let! bytesRead =
        stdin.ReadAsync(
          stdinBuffer,
          writeOffset,
          stdinBuffer.Length - writeOffset
        )

      if bytesRead = 0 then
        stdinEof <- true
        return false
      else
        stdinCount <- stdinCount + bytesRead
        return true
  }

  let appendBufferedBytes (writer: ArrayBufferWriter<byte>) offset count =
    if count > 0 then
      let target = writer.GetSpan(count)
      let source = ReadOnlySpan<byte>(stdinBuffer, offset, count)
      source.CopyTo(target)
      writer.Advance(count)

  let tryFindPattern (pattern: byte array) =
    if pattern.Length = 0 || pattern.Length > stdinCount then
      ValueNone
    else
      let mutable found = -1
      let mutable i = 0
      let limit = stdinCount - pattern.Length

      while found < 0 && i <= limit do
        let mutable matched = true
        let mutable j = 0

        while matched && j < pattern.Length do
          if stdinBuffer[stdinOffset + i + j] <> pattern[j] then
            matched <- false

          j <- j + 1

        if matched then found <- i else i <- i + 1

      if found < 0 then ValueNone else ValueSome found

  let prefixSuffixLength (pattern: byte array) =
    let maxCheck = min (pattern.Length - 1) stdinCount
    let mutable best = 0
    let mutable k = 1

    while k <= maxCheck do
      let mutable matched = true
      let mutable j = 0

      while matched && j < k do
        if stdinBuffer[stdinOffset + stdinCount - k + j] <> pattern[j] then
          matched <- false

        j <- j + 1

      if matched then
        best <- k

      k <- k + 1

    best

  let crBytes = inputEncoding.GetBytes("\r")
  let lfBytes = inputEncoding.GetBytes("\n")
  let crlfBytes = inputEncoding.GetBytes("\r\n")

  let tryFindLineTerminator () =
    let mutable bestIndex = Int32.MaxValue
    let mutable bestLength = 0

    let consider len candidate =
      match candidate with
      | ValueNone -> ()
      | ValueSome index ->
        if index < bestIndex || (index = bestIndex && len > bestLength) then
          bestIndex <- index
          bestLength <- len

    consider crlfBytes.Length (tryFindPattern crlfBytes)
    consider lfBytes.Length (tryFindPattern lfBytes)
    consider crBytes.Length (tryFindPattern crBytes)

    if bestIndex = Int32.MaxValue then
      ValueNone
    else
      ValueSome(bestIndex, bestLength)

  let pendingLinePrefixLength () =
    max
      (prefixSuffixLength crlfBytes)
      (max (prefixSuffixLength lfBytes) (prefixSuffixLength crBytes))

  let readlnCore () : Task<string option> = task {
    let line = ArrayBufferWriter<byte>()
    let mutable finished = false
    let mutable result = None

    while not finished do
      match tryFindLineTerminator () with
      | ValueSome(index, length) when
        length = crBytes.Length
        && index + length = stdinCount
        && not stdinEof
        && crlfBytes.Length > crBytes.Length
        ->
        let! _ = refillStdinBuffer ()
        ()
      | ValueSome(index, length) ->
        appendBufferedBytes line stdinOffset index
        consumeStdin (index + length)
        finished <- true
        result <- Some(inputEncoding.GetString(line.WrittenSpan))
      | ValueNone ->
        let keep = if stdinEof then 0 else pendingLinePrefixLength ()

        let available = stdinCount - keep
        appendBufferedBytes line stdinOffset available
        consumeStdin available

        if stdinCount = 0 && stdinEof then
          finished <- true

          if line.WrittenCount > 0 then
            result <- Some(inputEncoding.GetString(line.WrittenSpan))
        else
          let! _ = refillStdinBuffer ()
          ()

    return result
  }

  let readCore count : Task<byte array> = task {
    if count <= 0 then
      return [||]
    else
      let output = Array.zeroCreate<byte> count
      let mutable written = 0

      if stdinCount > 0 then
        let copied = min count stdinCount
        Buffer.BlockCopy(stdinBuffer, stdinOffset, output, 0, copied)
        consumeStdin copied
        written <- copied

      if written < count && not stdinEof then
        let! bytesRead = stdin.ReadAsync(output, written, count - written)

        if bytesRead = 0 then
          stdinEof <- true
        else
          written <- written + bytesRead

      return
        if written = output.Length then output
        elif written = 0 then [||]
        else output[.. written - 1]
  }

  let readToEndCore () : Task<byte array> = task {
    use ms = new MemoryStream()

    if stdinCount > 0 then
      ms.Write(stdinBuffer, stdinOffset, stdinCount)
      consumeStdin stdinCount

    if not stdinEof then
      do! stdin.CopyToAsync(ms)
      stdinEof <- true

    return ms.ToArray()
  }

  let readToStringCore () : Task<string> = task {
    let! bytes = readToEndCore ()
    return inputEncoding.GetString(bytes)
  }

  let writeTextCore (stream: Stream) (text: string) : Task<unit> = task {
    let byteCount = outputEncoding.GetMaxByteCount(text.Length)
    let rented = ArrayPool<byte>.Shared.Rent(byteCount)

    try
      let written = outputEncoding.GetBytes(text, 0, text.Length, rented, 0)
      do! stream.WriteAsync(rented, 0, written)
    finally
      ArrayPool<byte>.Shared.Return(rented)
  }

  let writeLineCore (stream: Stream) (text: string) : Task<unit> = task {
    do! writeTextCore stream text
    do! stream.WriteAsync(newLineBytes, 0, newLineBytes.Length)
  }

  static member Provider = provider

  interface Stdio with
    member _.eprint(arg1: string) =
      fun () -> withGate stderrGate (fun () -> writeTextCore stderr arg1)
      |> Eff.ofTask

    member _.eprintln(arg1: string) =
      fun () -> withGate stderrGate (fun () -> writeLineCore stderr arg1)
      |> Eff.ofTask

    member _.print(arg1: string) =
      fun () -> withGate stdoutGate (fun () -> writeTextCore stdout arg1)
      |> Eff.ofTask

    member _.println(arg1: string) =
      fun () -> withGate stdoutGate (fun () -> writeLineCore stdout arg1)
      |> Eff.ofTask

    member _.readln() =
      fun () -> withGate stdinGate readlnCore
      |> Eff.ofTask

    member _.read n =
      fun () -> withGate stdinGate (fun () -> readCore n)
      |> Eff.ofTask

    member _.readToString() =
      fun () -> withGate stdinGate readToStringCore
      |> Eff.ofTask

    member _.readToEnd() =
      fun () -> withGate stdinGate readToEndCore
      |> Eff.ofTask

    member _.write bytes =
      Eff.ofTask
      <| fun () ->
        withGate
          stdoutGate
          (fun () -> task { do! stdout.WriteAsync(bytes, 0, bytes.Length) })


    member _.ewrite bytes =
      Eff.ofTask
      <| fun () ->
        withGate
          stderrGate
          (fun () -> task { do! stderr.WriteAsync(bytes, 0, bytes.Length) })

    member _.flush() =
      fun () -> withGate stdoutGate (fun () -> task { do! stdout.FlushAsync() })
      |> Eff.ofTask

    member _.eflush() =
      fun () -> withGate stderrGate (fun () -> task { do! stderr.FlushAsync() })
      |> Eff.ofTask

    member _.isTerminal() = not Console.IsInputRedirected

    member _.isOutTerminal() = not Console.IsOutputRedirected

    member _.isErrTerminal() = not Console.IsErrorRedirected


[<AutoOpen>]
module StdioExt =
  type Stdio with
    static member Provider() = StdioProvider.Provider
