module EffSharp.Examples.Program

open EffSharp.Core
open EffSharp.Std
open System
open type EffSharp.Std.Console

type AppEnv() =
  interface Effect.Console with
    member _.Console = Console.Provider()

  interface Effect.Fs with
    member _.Fs = Fs.Provider()

  interface Effect.Clock with
    member _.Clock = Clock.Provider()

let program () = eff {
  let! now = Clock.now ()
  do! println $"starting program at {now}"

  let! contents = Fs.readText (Path "./Effects.fs")
  do! println $"file contents: {contents}"

  return ()
}

[<EntryPoint>]
let main _ =
  program () |> Eff.runSync (AppEnv()) |> printfn "%O"

  0
