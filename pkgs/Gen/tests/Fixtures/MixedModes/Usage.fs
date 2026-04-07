namespace MixedModesRed

open EffSharp.Core

module Usage =
  let logProgram () : Eff<unit, exn, #ILogger> = ILogger.info "hello"
  let clockProgram () : Eff<string, exn, #EClock> = IClock.now ()
