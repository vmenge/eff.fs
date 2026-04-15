module EffSharp.Examples.Program

open EffSharp.Core
open EffSharp.Std
open System
open type EffSharp.Std.Stdio

type AppEnv() =
  interface Effect.Stdio with
    member _.Stdio = Stdio.Provider()

  interface Effect.Fs with
    member _.Fs = Fs.Provider()

  interface Effect.Clock with
    member _.Clock = Clock.Provider()

  interface Effect.Env with
    member _.Env = Env.Provider()

  interface Effect.Random with
    member _.Random = Random.Provider()

let program () = eff {
  let! now = Clock.now ()
  do! println $"starting program at {now}"

  let! myvar = Env.get "MYVAR"
  do! println $"MYVAR: {myvar}"

  let! randomval = Random.intRange 1 101
  do! println $"random val: {randomval}"

  return ()
}

[<EntryPoint>]
let main _ =
  program () |> Eff.runSync (AppEnv()) |> printfn "%O"

  0
