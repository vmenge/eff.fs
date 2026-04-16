module SupportedSyncRed.Program

open EffSharp.Core

type Logger() =
  let mutable messages = []

  member _.Messages = List.rev messages

  interface ILogger with
    member _.Debug(message: string) =
      messages <- message :: messages

type Clock() =
  interface IClock with
    member _.Now() = "2026-04-06T00:00:00Z"

type Parser() =
  interface IParser with
    member _.Parse(input: string) =
      if input = "42" then Ok 42 else Error InvalidInput

type Lookup() =
  interface ILookup with
    member _.TryFind(id: int, name: string) =
      if id = 1 && name = "user" then
        Ok { Id = id; Name = "Ada" }
      else
        Error NotFound

type AppEnv(logger: ILogger, clock: IClock, parser: IParser, lookup: ILookup) =
  interface ILogger with
    member _.Debug(message: string) = logger.Debug(message)

  interface IClock with
    member _.Now() = clock.Now()

  interface IParser with
    member _.Parse(input: string) = parser.Parse(input)

  interface ILookup with
    member _.TryFind(id: int, name: string) = lookup.TryFind(id, name)

let private expectOk expected exit name =
  match exit with
  | Exit.Ok value when value = expected -> ()
  | Exit.Ok value -> failwithf "%s returned %A instead of %A" name value expected
  | Exit.Err err -> failwithf "%s returned managed error %A" name err
  | Exit.Aborted -> failwithf "%s was aborted" name
  | Exit.Exn ex -> raise ex

let run () =
  let logger = Logger()

  let env =
    AppEnv(logger :> ILogger, Clock() :> IClock, Parser() :> IParser, Lookup() :> ILookup)

  match Usage.logProgram () |> Eff.runSync env with
  | Exit.Ok () -> ()
  | Exit.Err err -> failwithf "logProgram returned managed error %A" err
  | Exit.Aborted -> failwith "logProgram was aborted"
  | Exit.Exn ex -> raise ex

  expectOk "2026-04-06T00:00:00Z" (Usage.clockProgram () |> Eff.runSync env) "clockProgram"
  expectOk 42 (Usage.parserProgram () |> Eff.runSync env) "parserProgram"
  expectOk { Id = 1; Name = "Ada" } (Usage.lookupProgram () |> Eff.runSync env) "lookupProgram"

  if logger.Messages <> [ "hello" ] then
    failwithf "logProgram should record one log message, got %A" logger.Messages

  "supported-sync-runtime-ok"
