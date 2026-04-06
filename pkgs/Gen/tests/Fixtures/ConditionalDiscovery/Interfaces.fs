namespace ConditionalDiscoveryRed

open EffSharp.Gen

#if EFFECTGEN_DISCOVERY
[<Effect>]
type IGreeter =
  abstract Greet: string -> string
#endif
