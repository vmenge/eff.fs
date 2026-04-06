namespace SupportedEffExactRed

open EffFs.Core

module Usage =
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #ERuntime> =
    ERuntime.spawn { Id = 1 }
