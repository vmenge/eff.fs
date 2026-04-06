namespace Gen.Fixtures.UnsupportedReturnShape

open EffSharp.Core
open EffSharp.Gen

type NeededEnv = { Value: int }

[<Effect>]
type IRunner =
  abstract spawn: int -> Eff<string, string, NeededEnv>
