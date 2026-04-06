namespace SupportedEffExactRed

open EffSharp.Core
open EffSharp.Gen

[<Effect>]
type IRuntime =
  abstract Spawn: Job -> Eff<JobHandle<JobResult>, SpawnError, unit>
