namespace WrappedNamingRed

open EffSharp.Core

module Usage =
  let greetProgram () : Eff<string, exn, #EGreeter> = IGreeter.greet "Ada"
  let logProgram () : Eff<unit, exn, #ELogger> = Logger.debug "hello"
  let oddLogProgram () : Eff<unit, exn, #EIlogger> = Ilogger.trace "odd"
