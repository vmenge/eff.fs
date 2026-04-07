module EffSharp.Examples.Program

open EffSharp.Core
open System

module Fs =
  let Provider =
    { new Fs with
        member _.delete(arg1: string) : Eff<unit, string, unit> =
          printfn $"deleted {arg1}"
          Pure()

        member _.read(arg1: string) : Eff<string, string, unit> =
          Pure $"contents from {arg1}"

        member _.write
          (arg1: string)
          (_arg2: byte array)
          : Eff<unit, string, unit> =
          printfn $"wrote to {arg1}"
          Pure()
    }

type AppEnv() =
  interface Log with
    member _.info msg = printfn "%s" msg

  interface Clock with
    member _.now() = DateTime.Now

  interface Effect.Fs with
    member _.Fs: Fs = Fs.Provider



let program () = eff {
  let! now = Clock.now ()
  do! Log.info $"starting program at {now}"

  let! contents = Fs.read "file"
  do! Log.info $"file contents: {contents}"
  do! Fs.delete "file"

  return ()
}

[<EntryPoint>]
let main _ =
  program () |> Eff.runSync (AppEnv()) |> ignore

  0
