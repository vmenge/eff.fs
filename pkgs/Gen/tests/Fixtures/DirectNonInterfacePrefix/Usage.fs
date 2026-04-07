namespace DirectNonInterfacePrefixRed

open EffSharp.Core

module Usage =
  let logProgram () : Eff<unit, exn, #Logger> = Logger.debug "hello"
