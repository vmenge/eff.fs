module SupportedEffExactRed.Program

open System
open EffSharp.Core

type Runtime() =
  interface IRuntime with
    member _.Spawn(job: Job) =
      Pure {
        Id = job.Id
        Result = Some { ExitCode = 0 }
      }

type AppEnv(runtime: IRuntime) =
  interface IRuntime with
    member _.Spawn(job: Job) = runtime.Spawn(job)

let run () =
  let env = AppEnv(Runtime() :> IRuntime)

  match Usage.spawnProgram () |> Eff.runSync env with
  | Exit.Ok value when value = { Id = 1; Result = Some { ExitCode = 0 } } ->
      "supported-eff-exact-runtime-ok"
  | Exit.Ok value -> failwithf "spawnProgram returned %A" value
  | Exit.Err err -> failwithf "spawnProgram returned managed error %A" err
  | Exit.Aborted -> failwith "spawnProgram was aborted"
  | Exit.Exn ex -> raise ex
