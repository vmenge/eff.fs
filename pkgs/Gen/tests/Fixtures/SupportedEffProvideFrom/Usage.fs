namespace SupportedEffProvideFromRed

open EffSharp.Core

module Usage =
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #ERuntimeService> =
    ERuntimeService.spawn { Id = 7 }
