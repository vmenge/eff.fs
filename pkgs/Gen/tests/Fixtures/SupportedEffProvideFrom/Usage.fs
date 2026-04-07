namespace SupportedEffProvideFromRed

open EffSharp.Core

module Usage =
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #ERuntimeService> =
    IRuntimeService.spawn { Id = 7 }
