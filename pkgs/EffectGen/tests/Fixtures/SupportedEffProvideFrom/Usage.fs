namespace SupportedEffProvideFromRed

open EffFs.Core

module Usage =
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #ERuntimeService> =
    ERuntimeService.spawn { Id = 7 }
