namespace EffSharp.Std

open System
open System.Text
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

    let parse (s: string) : Cmd =
      let tokens = ResizeArray<string>()
      let current = StringBuilder()
      let mutable quoteChar = ValueNone

      let flush () =
        if current.Length > 0 then
          tokens.Add(current.ToString())
          current.Clear() |> ignore

      for ch in s do
        match quoteChar with
        | ValueSome q when ch = q ->
          quoteChar <- ValueNone
        | ValueSome _ ->
          current.Append(ch) |> ignore
        | ValueNone ->
          match ch with
          | '"'
          | ''' -> quoteChar <- ValueSome ch
          | c when Char.IsWhiteSpace(c) -> flush ()
          | c -> current.Append(c) |> ignore

      flush ()

      match tokens |> Seq.toList with
      | [] -> create "" []
      | program :: args -> create program args

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

    let pipe (left: Cmd) (right: Cmd) : Cmd =
      { right with Stdin = FromCmd { left with Stdout = Piped } }

    let output (cmd: Cmd) : Eff<Output, CommandErr, #Effect.Command> =
      let cmd = {
        cmd with
            Stdout = Piped
            Stderr = Piped
            Stdin =
              match cmd.Stdin with
              | Inherit -> Null
              | other -> other
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

  type PipeOp = PipeOp with
    static member Resolve(_: PipeOp, cmd: Cmd) = cmd
    static member Resolve(_: PipeOp, s: string) = Cmd.parse s

  let inline (|.) (left: ^L) (right: ^R) : Cmd =
    let l =
      ((^L or PipeOp) :
        (static member Resolve : PipeOp * ^L -> Cmd)
        (PipeOp, left))

    let r =
      ((^R or PipeOp) :
        (static member Resolve : PipeOp * ^R -> Cmd)
        (PipeOp, right))

    Cmd.pipe l r
