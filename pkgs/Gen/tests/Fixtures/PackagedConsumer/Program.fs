module PackagedConsumer.Program

open System
open EffSharp.Core

type ConsoleGreeter() =
  interface IGreeter with
    member _.Greet(name: string) = $"Hello, {name}."

type AppEnv() =
  let greeter = ConsoleGreeter() :> IGreeter

  interface EGreeter with
    member _.Greeter = greeter

let run () =
  let result =
    EGreeter.greet "packaged consumer"
    |> Eff.runSync (AppEnv())

  match result with
  | Exit.Ok greeting -> greeting
  | Exit.Err err -> failwithf "expected greeting from packaged consumer, got managed error: %A" err
  | Exit.Exn ex -> raise ex
