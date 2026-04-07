module EffSharp.Examples.Program

open EffSharp.Core
open System

type AppEnv() =
  interface Log with
    member _.info msg = printfn "%s" msg

  interface Clock with
    member _.now() = DateTime.Now

  interface Fs with
    member _.readToString _path = Pure "contents"


let program () = eff {
  let! now = Clock.now ()
  do! Log.info $"starting program at {now}"

  let! contents = Fs.readToString "filepath"
  do! Log.info $"file contents are {contents}"

  return ()
}

[<EntryPoint>]
let main _ =
  program () |> Eff.runSync (AppEnv()) |> ignore

  0
