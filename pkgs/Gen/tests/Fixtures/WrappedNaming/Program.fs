module WrappedNamingRed.Program

open EffSharp.Core

type GreeterService() =
  interface IGreeter with
    member _.Greet(name: string) = $"Hello, {name}."

type LoggerService() =
  let mutable messages = []

  member _.Messages = List.rev messages

  interface Logger with
    member _.Debug(message: string) =
      messages <- message :: messages

type OddLoggerService() =
  let mutable messages = []

  member _.Messages = List.rev messages

  interface Ilogger with
    member _.Trace(message: string) =
      messages <- message :: messages

type AppEnv(greeter: IGreeter, logger: Logger, oddLogger: Ilogger) =
  interface Effect.Greeter with
    member _.Greeter = greeter

  interface Effect.Logger with
    member _.Logger = logger

  interface Effect.Ilogger with
    member _.Ilogger = oddLogger

let private expectOk expected exit name =
  match exit with
  | Exit.Ok value when value = expected -> ()
  | Exit.Ok value -> failwithf "%s returned %A instead of %A" name value expected
  | Exit.Err err -> failwithf "%s returned managed error %A" name err
  | Exit.Aborted -> failwithf "%s was aborted" name
  | Exit.Exn ex -> raise ex

let run () =
  let logger = LoggerService()
  let oddLogger = OddLoggerService()

  let env =
    AppEnv(
      GreeterService() :> IGreeter,
      logger :> Logger,
      oddLogger :> Ilogger
    )

  expectOk "Hello, Ada." (Usage.greetProgram () |> Eff.runSync env) "greetProgram"

  match Usage.logProgram () |> Eff.runSync env with
  | Exit.Ok () -> ()
  | Exit.Err err -> failwithf "logProgram returned managed error %A" err
  | Exit.Aborted -> failwith "logProgram was aborted"
  | Exit.Exn ex -> raise ex

  match Usage.oddLogProgram () |> Eff.runSync env with
  | Exit.Ok () -> ()
  | Exit.Err err -> failwithf "oddLogProgram returned managed error %A" err
  | Exit.Aborted -> failwith "oddLogProgram was aborted"
  | Exit.Exn ex -> raise ex

  if logger.Messages <> [ "hello" ] then
    failwithf "logProgram should record one log message, got %A" logger.Messages

  if oddLogger.Messages <> [ "odd" ] then
    failwithf "oddLogProgram should record one log message, got %A" oddLogger.Messages

  "wrapped-naming-runtime-ok"
