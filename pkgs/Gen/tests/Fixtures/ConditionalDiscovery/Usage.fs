namespace ConditionalDiscoveryRed

open EffSharp.Core

module Usage =
  let greetProgram () : Eff<string, exn, #EGreeter> = EGreeter.greet "Ada"
