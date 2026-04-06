namespace EffectGen.Fixtures.DuplicateGeneratedNames

open EffFs.EffectGen

[<Effect>]
type ILogger =
  abstract Log: string -> unit

[<Effect>]
type Logger =
  abstract Write: string -> unit
