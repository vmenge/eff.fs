namespace SupportedGenericEnvWrapRed

open EffSharp.Core

module Usage =
  let greetProgram () : Eff<string, exn, #Effect.Greeter> = IGreeter.Greet "Ada"
