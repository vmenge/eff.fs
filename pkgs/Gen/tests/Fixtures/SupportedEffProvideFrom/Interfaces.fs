namespace SupportedEffProvideFromRed

open EffSharp.Core
open EffSharp.Gen

type IRuntimeEnv =
  abstract RuntimeService: IRuntimeService

and [<Effect>] IRuntimeService =
  abstract Spawn: Job -> Eff<JobHandle<JobResult>, SpawnError, IRuntimeEnv>
