namespace EffSharp.Gen.Tests

open System
open System.IO
open System.Threading.Tasks
open Expecto

module HarnessTests =
  open Harness

  let private writeVerboseScript () =
    let scriptPath = Path.Combine(Path.GetTempPath(), $"effectgen-harness-{Guid.NewGuid():N}.sh")

    File.WriteAllText(
      scriptPath,
      """#!/bin/sh
i=0
while [ "$i" -lt 20000 ]; do
  printf 'stdout-%05d\n' "$i"
  printf 'stderr-%05d\n' "$i" >&2
  i=$((i + 1))
done
"""
    )

    scriptPath

  let tests =
    testList "HarnessTests" [
      testTask "process output is drained while the process is running" {
        let scriptPath = writeVerboseScript ()

        try
          let processTask = runProcess None "sh" $"\"{scriptPath}\""
          let! completed = Task.WhenAny(processTask, Task.Delay(TimeSpan.FromSeconds(10.0)))

          Expect.isTrue (obj.ReferenceEquals(completed, processTask)) "the harness should complete instead of hanging on a full stdout/stderr buffer"

          let! result = processTask
          Expect.equal result.ExitCode 0 "the verbose script should exit successfully"
          Expect.stringContains result.Output "stdout-19999" "the harness should capture the end of stdout"
          Expect.stringContains result.Output "stderr-19999" "the harness should capture the end of stderr"
        finally
          if File.Exists(scriptPath) then
            File.Delete(scriptPath)
      }
    ]
