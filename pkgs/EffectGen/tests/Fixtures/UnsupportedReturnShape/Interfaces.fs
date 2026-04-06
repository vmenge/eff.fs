namespace EffectGen.Fixtures.UnsupportedReturnShape

open EffFs.Core
open EffFs.EffectGen

type NeededEnv = { Value: int }

[<Effect>]
type IRunner =
  abstract spawn: int -> Eff<string, string, NeededEnv>
