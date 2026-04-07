namespace ConditionalDiscoveryRed

open EffSharp.Core

module Usage =
  let greetProgram () : Eff<string, exn, #IGreeter> = IGreeter.greet "Ada"
