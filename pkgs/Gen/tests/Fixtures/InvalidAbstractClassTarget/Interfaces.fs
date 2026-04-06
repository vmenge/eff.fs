namespace Gen.Fixtures.InvalidAbstractClassTarget

open EffSharp.Gen

[<AbstractClass>]
[<Effect>]
type BadService =
  abstract Fetch: string -> string
