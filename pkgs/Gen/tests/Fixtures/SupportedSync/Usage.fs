namespace SupportedSyncRed

open EffSharp.Core

module Usage =
  let logProgram () : Eff<unit, exn, #ELogger> = ELogger.debug "hello"
  let clockProgram () : Eff<string, exn, #EClock> = EClock.now ()
  let parserProgram () : Eff<int, ParseError, #EParser> = EParser.parse "42"
  let lookupProgram () : Eff<User, LookupError, #ELookup> = ELookup.tryFind (1, "user")
