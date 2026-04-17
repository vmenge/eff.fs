namespace EffSharp.Std.Tests

open Expecto
open EffSharp.Core
open EffSharp.Std

module Cmd =
  let private parse =
    testList "parse" [
      testCase "single program without args" (fun () ->
        let cmd = Cmd.parse "ls"
        Expect.equal cmd.Program "ls" ""
        Expect.equal cmd.Args [] ""
      )

      testCase "program with args" (fun () ->
        let cmd = Cmd.parse "ls -la"
        Expect.equal cmd.Program "ls" ""
        Expect.equal cmd.Args [ "-la" ] ""
      )

      testCase "multiple args" (fun () ->
        let cmd = Cmd.parse "grep -i -r pattern"
        Expect.equal cmd.Program "grep" ""
        Expect.equal cmd.Args [ "-i"; "-r"; "pattern" ] ""
      )

      testCase "single-quoted arg" (fun () ->
        let cmd = Cmd.parse "grep -i 'hello world'"
        Expect.equal cmd.Program "grep" ""
        Expect.equal cmd.Args [ "-i"; "hello world" ] ""
      )

      testCase "double-quoted arg" (fun () ->
        let cmd = Cmd.parse "echo \"hello world\""
        Expect.equal cmd.Program "echo" ""
        Expect.equal cmd.Args [ "hello world" ] ""
      )

      testCase "trims leading and trailing whitespace" (fun () ->
        let cmd = Cmd.parse "  ls  -la  "
        Expect.equal cmd.Program "ls" ""
        Expect.equal cmd.Args [ "-la" ] ""
      )

      testCase "empty string produces empty program" (fun () ->
        let cmd = Cmd.parse ""
        Expect.equal cmd.Program "" ""
        Expect.equal cmd.Args [] ""
      )

      testCase "defaults to Inherit stdio" (fun () ->
        let cmd = Cmd.parse "ls"
        Expect.equal cmd.Stdin Inherit ""
        Expect.equal cmd.Stdout Inherit ""
        Expect.equal cmd.Stderr Inherit ""
      )
    ]

  let private pipe =
    testList "pipe" [
      testCase "sets right stdin to FromCmd of left with Piped stdout"
        (fun () ->
          let left = Cmd.create "ls" [ "-la" ]
          let right = Cmd.create "grep" [ ".fs" ]
          let piped = Cmd.pipe left right

          Expect.equal piped.Program "grep" ""
          Expect.equal piped.Args [ ".fs" ] ""

          match piped.Stdin with
          | FromCmd upstream ->
            Expect.equal upstream.Program "ls" ""
            Expect.equal upstream.Args [ "-la" ] ""
            Expect.equal upstream.Stdout Piped ""
          | other ->
            failtest $"expected FromCmd, got %A{other}"
        )

      testCase "three-stage chain nests correctly" (fun () ->
        let a = Cmd.create "ls" []
        let b = Cmd.create "grep" [ ".fs" ]
        let c = Cmd.create "wc" [ "-l" ]
        let piped = Cmd.pipe a b |> Cmd.pipe <| c

        Expect.equal piped.Program "wc" ""

        match piped.Stdin with
        | FromCmd mid ->
          Expect.equal mid.Program "grep" ""
          Expect.equal mid.Stdout Piped ""

          match mid.Stdin with
          | FromCmd first ->
            Expect.equal first.Program "ls" ""
            Expect.equal first.Stdout Piped ""
          | other ->
            failtest $"expected inner FromCmd, got %A{other}"
        | other ->
          failtest $"expected outer FromCmd, got %A{other}"
      )
    ]

  let private pipeOperator =
    testList "|." [
      testCase "string |. string" (fun () ->
        let cmd = "echo hello" |. "cat"

        Expect.equal cmd.Program "cat" ""

        match cmd.Stdin with
        | FromCmd upstream ->
          Expect.equal upstream.Program "echo" ""
          Expect.equal upstream.Args [ "hello" ] ""
        | other -> failtest $"expected FromCmd, got %A{other}"
      )

      testCase "Cmd |. string" (fun () ->
        let cmd = Cmd.create "echo" [ "hello" ] |. "cat"

        Expect.equal cmd.Program "cat" ""

        match cmd.Stdin with
        | FromCmd upstream ->
          Expect.equal upstream.Program "echo" ""
          Expect.equal upstream.Args [ "hello" ] ""
        | other -> failtest $"expected FromCmd, got %A{other}"
      )

      testCase "three-way string chain" (fun () ->
        let cmd = "ls -la" |. "grep .fs" |. "wc -l"

        Expect.equal cmd.Program "wc" ""
        Expect.equal cmd.Args [ "-l" ] ""

        match cmd.Stdin with
        | FromCmd mid ->
          Expect.equal mid.Program "grep" ""

          match mid.Stdin with
          | FromCmd first ->
            Expect.equal first.Program "ls" ""
          | other ->
            failtest $"expected inner FromCmd, got %A{other}"
        | other ->
          failtest $"expected outer FromCmd, got %A{other}"
      )
    ]

  type private AppEnv() =
    interface Effect.Command with
      member _.Command = Command.Provider()

  let private integration =
    testList "integration" [
      testTask "piped commands execute end to end" {
        let env = AppEnv()

        let! result =
          "echo hello" |. "cat" |> Cmd.output |> Eff.runTask env

        let output = Exit.ok result

        let stdout =
          System.Text.Encoding.UTF8
            .GetString(output.Stdout)
            .Trim()

        Expect.equal stdout "hello" ""
        Expect.equal output.ExitCode 0 ""
      }

      testTask "three-stage pipeline" {
        let env = AppEnv()

        let! result =
          "printf 'a\nb\nc'" |. "sort -r" |. "head -1"
          |> Cmd.output
          |> Eff.runTask env

        let output = Exit.ok result

        let stdout =
          System.Text.Encoding.UTF8
            .GetString(output.Stdout)
            .Trim()

        Expect.equal stdout "c" ""
      }
    ]

  let tests =
    testList "Cmd" [ parse; pipe; pipeOperator; integration ]
