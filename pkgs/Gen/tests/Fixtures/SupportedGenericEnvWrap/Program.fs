module SupportedGenericEnvWrapRed.Program

open EffSharp.Core

type GreeterService() =
  interface IGreeter with
    member _.Greet(name: string) = Pure $"Hello, {name}."

type AppEnv(greeter: IGreeter) =
  interface Effect.Greeter with
    member _.Greeter = greeter

let run () =
  let env = AppEnv(GreeterService() :> IGreeter)

  match Usage.greetProgram () |> Eff.runSync env with
  | Exit.Ok "Hello, Ada." -> "supported-generic-env-wrap-runtime-ok"
  | Exit.Ok value -> failwithf "greetProgram returned %A" value
  | Exit.Err err -> failwithf "greetProgram returned managed error %A" err
  | Exit.Aborted -> failwith "greetProgram was aborted"
  | Exit.Exn ex -> raise ex
