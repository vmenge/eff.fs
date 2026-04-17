namespace EffSharp.Std

open EffSharp.Core

[<AutoOpen>]
module CmdExt =
  module Cmd =
    let create program args = {
      Program = program
      Args = args
      EnvVars = []
      ClearEnv = false
      WorkDir = None
      Stdin = Inherit
      Stdout = Inherit
      Stderr = Inherit
    }

    let arg value cmd = { cmd with Args = cmd.Args @ [ value ] }
    let args values cmd = { cmd with Args = cmd.Args @ values }

    let env key value cmd = {
      cmd with
          EnvVars = cmd.EnvVars @ [ key, value ]
    }

    let clearEnv cmd = { cmd with ClearEnv = true }
    let workDir path cmd = { cmd with WorkDir = Some path }
    let stdin stdio cmd = { cmd with Stdin = stdio }
    let stdout stdio cmd : Cmd = { cmd with Stdout = stdio }
    let stderr stdio cmd : Cmd = { cmd with Stderr = stdio }

    let output (cmd: Cmd) : Eff<Output, CommandErr, #Effect.Command> =
      let cmd = {
        cmd with
            Stdout = Piped
            Stderr = Piped
            Stdin = Null
      }

      eff {
        let! child = Command.spawn cmd
        let! stdout = Child.readAllStdout child
        let! stderr = Child.readAllStderr child
        let! exitCode = child.Wait()

        return {
          ExitCode = exitCode
          Stdout = stdout
          Stderr = stderr
        }
      }

    let status (cmd: Cmd) : Eff<int, CommandErr, #Effect.Command> = eff {
      let! child = Command.spawn cmd
      return! child.Wait()
    }
