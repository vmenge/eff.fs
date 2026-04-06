namespace EffectGen.Fixtures.UnsupportedMemberKind

open EffFs.EffectGen

[<Effect>]
type IThing =
  abstract Name: string
  abstract Run: int -> unit
