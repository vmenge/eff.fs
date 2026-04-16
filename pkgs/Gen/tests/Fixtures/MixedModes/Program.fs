module MixedModesRed.Program

open EffSharp.Core

type LoggerService() =
  let mutable messages = []

  member _.Messages = List.rev messages

  interface ILogger with
    member _.Info(message: string) =
      messages <- message :: messages

type ClockService() =
  interface IClock with
    member _.Now() = "2026-04-07T00:00:00Z"

type AppEnv(logger: ILogger, clock: IClock) =
  interface ILogger with
    member _.Info(message: string) = logger.Info(message)

  interface Effect.Clock with
    member _.Clock = clock

let private expectOk expected exit name =
  match exit with
  | Exit.Ok value when value = expected -> ()
  | Exit.Ok value -> failwithf "%s returned %A instead of %A" name value expected
  | Exit.Err err -> failwithf "%s returned managed error %A" name err
  | Exit.Aborted -> failwithf "%s was aborted" name
  | Exit.Exn ex -> raise ex

let run () =
  let logger = LoggerService()
  let env = AppEnv(logger :> ILogger, ClockService() :> IClock)

  match Usage.logProgram () |> Eff.runSync env with
  | Exit.Ok () -> ()
  | Exit.Err err -> failwithf "logProgram returned managed error %A" err
  | Exit.Aborted -> failwith "logProgram was aborted"
  | Exit.Exn ex -> raise ex

  expectOk "2026-04-07T00:00:00Z" (Usage.clockProgram () |> Eff.runSync env) "clockProgram"

  if logger.Messages <> [ "hello" ] then
    failwithf "logProgram should record one log message, got %A" logger.Messages

  "mixed-modes-runtime-ok"
