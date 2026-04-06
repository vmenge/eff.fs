module EffFs.Examples.Program

open EffFs.Core

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
  greetingProgram "EffectGen"
  |> Eff.runSync (AppEnv())
