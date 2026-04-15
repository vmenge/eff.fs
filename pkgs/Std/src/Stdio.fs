namespace EffSharp.Std

open EffSharp.Gen
open System.Threading.Tasks

[<Effect(Mode.Wrap)>]
type Stdio =
  abstract print: string -> unit Task
  abstract println: string -> unit Task
  abstract eprint: string -> unit Task
  abstract eprintln: string -> unit Task

  abstract readln: unit -> (string option) Task
  abstract read: int -> (byte array) Task
  abstract readToString: unit -> string Task
  abstract readToEnd: unit -> (byte array) Task

  abstract write: byte array -> unit Task
  abstract ewrite: byte array -> unit Task

  abstract flush: unit -> unit Task
  abstract eflush: unit -> unit Task

  abstract isTerminal: unit -> bool
  abstract isOutTerminal: unit -> bool
  abstract isErrTerminal: unit -> bool

type StdioProvider internal () =
  let stdin = System.Console.OpenStandardInput()
  let stdout = System.Console.OpenStandardOutput()
  let stderr = System.Console.OpenStandardError()

  interface Stdio with
    member _.eprint(arg1: string) = task {
      do! System.Console.Error.WriteAsync arg1
    }

    member _.eprintln(arg1: string) = task {
      do! System.Console.Error.WriteLineAsync arg1
    }

    member _.print(arg1: string) = task {
      do! System.Console.Out.WriteAsync arg1
    }

    member _.println(arg1: string) = task {
      do! System.Console.Out.WriteLineAsync arg1
    }

    member _.readln() = task {
      let! line = System.Console.In.ReadLineAsync()
      return Option.ofObj line
    }

    member _.read n = task {
      let buf = Array.zeroCreate n
      let! bytesRead = stdin.ReadAsync(buf, 0, n)

      return buf[.. bytesRead - 1]
    }

    member _.readToEnd() = task {
      use ms = new System.IO.MemoryStream()
      do! stdin.CopyToAsync ms
      return ms.ToArray()
    }

    member _.readToString() = task {
      return! System.Console.In.ReadToEndAsync()
    }

    member _.write bytes = task {
      do! stdout.WriteAsync(bytes, 0, bytes.Length)
    }

    member _.ewrite bytes = task {
      do! stderr.WriteAsync(bytes, 0, bytes.Length)
    }

    member _.flush() = task { do! stdout.FlushAsync() }

    member _.eflush() = task { do! stdout.FlushAsync() }

    member _.isTerminal() = not System.Console.IsInputRedirected

    member _.isOutTerminal() = not System.Console.IsOutputRedirected

    member _.isErrTerminal() = not System.Console.IsErrorRedirected


[<AutoOpen>]
module StdioExt =
  type Stdio with
    static member Provider() = StdioProvider()
