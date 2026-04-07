namespace DirectNonInterfacePrefixRed

open EffSharp.Gen

[<Effect>]
type Logger =
  abstract Debug: string -> unit
