module SupportedEffProvideFromRed.Program

open System
open EffSharp.Core

type RuntimeService() =
  interface IRuntimeService with
    member _.Spawn(job: Job) =
      Eff.read (fun (_: IRuntimeEnv) ->
        {
          Id = job.Id
          Result = Some { Value = job.Id * 2 }
        })

type AppEnv(runtimeService: IRuntimeService) =
  interface Effect.RuntimeService with
    member _.RuntimeService = runtimeService

let run () =
  let env = AppEnv(RuntimeService() :> IRuntimeService)

  match Usage.spawnProgram () |> Eff.runSync env with
  | Exit.Ok value when value = { Id = 7; Result = Some { Value = 14 } } ->
      "supported-eff-providefrom-runtime-ok"
  | Exit.Ok value -> failwithf "spawnProgram returned %A" value
  | Exit.Err err -> failwithf "spawnProgram returned managed error %A" err
  | Exit.Aborted -> failwith "spawnProgram was aborted"
  | Exit.Exn ex -> raise ex
