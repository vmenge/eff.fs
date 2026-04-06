namespace Gen.Fixtures.UnsupportedMemberKind

open EffSharp.Gen

[<Effect>]
type IThing =
  abstract Name: string
  abstract Run: int -> unit
