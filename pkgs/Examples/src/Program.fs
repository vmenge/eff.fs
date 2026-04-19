module EffSharp.Examples.Program

open EffSharp.Core
open EffSharp.Std
open System
open type EffSharp.Std.Stdio
open System.Text

let program () = effr {
  let! out = "ls" |. "grep -i .fs" |> Cmd.output
  do! String.fromUtf8 out.Stdout |> Eff.bind println
  do! Stdio.println "hello, world!"

  let! test = "echo test" |> Cmd.output
  do! String.fromUtf8 test.Stdout |> Eff.bind println

  let! num = Random.float ()

  return ()
}


[<EntryPoint>]
let main _ =
  program () |> Eff.runSync (Std.Provider()) |> printfn "%O"

  0
