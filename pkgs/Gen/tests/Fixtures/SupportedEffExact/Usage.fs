namespace SupportedEffExactRed

open EffSharp.Core

module Usage =
  let spawnProgram () : Eff<JobHandle<JobResult>, SpawnError, #IRuntime> =
    IRuntime.spawn { Id = 1 }
