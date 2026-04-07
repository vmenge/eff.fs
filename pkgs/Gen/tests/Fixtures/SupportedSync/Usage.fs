namespace SupportedSyncRed

open EffSharp.Core

module Usage =
  let logProgram () : Eff<unit, exn, #ILogger> = ILogger.debug "hello"
  let clockProgram () : Eff<string, exn, #IClock> = IClock.now ()
  let parserProgram () : Eff<int, ParseError, #IParser> = IParser.parse "42"
  let lookupProgram () : Eff<User, LookupError, #ILookup> = ILookup.tryFind (1, "user")
