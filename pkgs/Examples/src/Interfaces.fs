namespace EffSharp.Examples

open EffSharp.Gen

[<Effect>]
type IGreeter =
  abstract Greet: string -> string
