namespace EffSharp.Std

open EffSharp.Gen
open System.Threading.Tasks

[<Effect(Mode.Wrap)>]
type Console =
  abstract print: string -> unit Task
  abstract println: string -> unit Task
  abstract error: string -> unit Task
  abstract errorln: string -> unit Task

type ConsoleProvider internal () =
  interface Console with
    member _.error(arg1: string) = task {
      do! System.Console.Error.WriteAsync arg1
    }

    member _.errorln(arg1: string) = task {
      do! System.Console.Error.WriteLineAsync arg1
    }

    member _.print(arg1: string) = task {
      do! System.Console.Out.WriteAsync arg1
    }

    member _.println(arg1: string) = task {
      do! System.Console.Out.WriteLineAsync arg1
    }

[<AutoOpen>]
module ConsoleExt =
  type Console with
    static member Provider() = ConsoleProvider()
