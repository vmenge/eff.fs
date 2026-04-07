namespace MixedModesRed

open EffSharp.Gen

[<Effect>]
type ILogger =
  abstract Info: string -> unit

[<Effect(Mode.Wrap)>]
type IClock =
  abstract Now: unit -> string
