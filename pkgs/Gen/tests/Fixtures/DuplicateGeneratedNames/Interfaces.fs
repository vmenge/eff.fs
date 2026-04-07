namespace Gen.Fixtures.DuplicateGeneratedNames

open EffSharp.Gen

[<Effect(Mode.Wrap)>]
type ILogger =
  abstract Log: string -> unit

[<Effect(Mode.Wrap)>]
type Logger =
  abstract Write: string -> unit
