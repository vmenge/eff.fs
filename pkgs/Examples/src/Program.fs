module EffSharp.Examples.Program

open EffSharp.Core

type ConsoleGreeter() =
  interface IGreeter with
    member _.Greet(name: string) = $"Hello, {name}."

type AppEnv() =
  let greeter = ConsoleGreeter() :> IGreeter

  interface EGreeter with
    member _.Greeter = greeter

let greetingProgram (name: string) : Eff<string, exn, #EGreeter> =
  EGreeter.greet name

let exampleGreeting () =
  greetingProgram "Gen"
  |> Eff.runSync (AppEnv())

let run () =
  match exampleGreeting () with
  | Exit.Ok greeting ->
      greeting
  | Exit.Err err -> failwithf "expected greeting, got managed error %A" err
  | Exit.Exn ex -> raise ex
