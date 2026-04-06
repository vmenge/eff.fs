namespace SupportedEffExactRed

open EffSharp.Core

module Usage =
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #ERuntime> =
    ERuntime.spawn { Id = 1 }
