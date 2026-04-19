namespace SupportedGenericEnvWrapRed

open EffSharp.Core
open EffSharp.Gen

[<Effect(Mode.Wrap)>]
type IGreeter =
  abstract Greet: string -> Eff<string, exn, 'env>
