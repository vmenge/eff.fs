namespace SupportedEffProvideFromRed

open EffSharp.Core
open EffSharp.Gen

type IRuntimeEnv =
  abstract RuntimeService: IRuntimeService

and [<Effect(Mode.Wrap)>] IRuntimeService =
  abstract Spawn: Job -> Eff<JobHandle<JobResult>, SpawnError, IRuntimeEnv>
