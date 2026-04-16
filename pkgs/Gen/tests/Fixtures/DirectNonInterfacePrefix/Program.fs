module DirectNonInterfacePrefixRed.Program

open EffSharp.Core

type LoggerImpl() =
  let mutable messages = []

  member _.Messages = List.rev messages

  interface Logger with
    member _.Debug(message: string) =
      messages <- message :: messages

type AppEnv(logger: Logger) =
  interface Logger with
    member _.Debug(message: string) = logger.Debug(message)

let run () =
  let logger = LoggerImpl()
  let env = AppEnv(logger :> Logger)

  match Usage.logProgram () |> Eff.runSync env with
  | Exit.Ok () when logger.Messages = [ "hello" ] -> "direct-non-interface-prefix-runtime-ok"
  | Exit.Ok () -> failwithf "logProgram should record one log message, got %A" logger.Messages
  | Exit.Err err -> failwithf "logProgram returned managed error %A" err
  | Exit.Aborted -> failwith "logProgram was aborted"
  | Exit.Exn ex -> raise ex
