namespace EffSharp.Std

open EffSharp.Core
open EffSharp.Gen
open System.ComponentModel
open System.Runtime.InteropServices
open System.Diagnostics

[<Struct>]
type CommandErr =
  | NotFound of program: string
  | PermissionDenied of exn
  | CommandIOError of exn

module CommandErr =
  let ofExn (platform: OSPlatform) (e: exn) : CommandErr =
    match e with
    | :? Win32Exception as we ->
      if platform = OSPlatform.Windows then
        match we.NativeErrorCode with
        | 2
        | 3 -> NotFound we.Message
        | 5 -> PermissionDenied we
        | _ -> CommandIOError e
      else
        match we.NativeErrorCode with
        | 2 -> NotFound we.Message
        | 13 -> PermissionDenied we
        | _ -> CommandIOError e
    | _ -> CommandIOError e

type Child =
  abstract Pid: int
  abstract Stdin: System.IO.Stream option
  abstract Stdout: System.IO.Stream option
  abstract Stderr: System.IO.Stream option
  abstract Wait: unit -> Eff<int, CommandErr, 'env>
  abstract Kill: unit -> Eff<unit, CommandErr, 'env>

[<Struct>]
type ProcessStdio =
  | Inherit
  | Piped
  | Null
  | FromChild of Child
  | FromCmd of upstream: Cmd

and Cmd = {
  Program: string
  Args: string list
  EnvVars: (string * string) list
  ClearEnv: bool
  WorkDir: Path option
  Stdin: ProcessStdio
  Stdout: ProcessStdio
  Stderr: ProcessStdio
}

[<Struct>]
type Output = {
  ExitCode: int
  Stdout: byte array
  Stderr: byte array
}

module Child =
  let writeStdin
    (data: byte array)
    (child: Child)
    : Eff<unit, CommandErr, 'env> =
    match child.Stdin with
    | None -> Err(CommandIOError(exn "stdin is not piped"))
    | Some stream ->
      Eff.tryTask (fun () -> task {
        do! stream.WriteAsync(data, 0, data.Length)
      })
      |> Eff.mapErr CommandIOError

  let closeStdin (child: Child) : Eff<unit, CommandErr, 'env> =
    match child.Stdin with
    | None -> Pure()
    | Some stream ->
      Eff.tryCatch (fun () -> stream.Close()) |> Eff.mapErr CommandIOError

  let readStdout (n: int) (child: Child) : Eff<byte array, CommandErr, 'env> =
    match child.Stdout with
    | None -> Err(CommandIOError(exn "stdout is not piped"))
    | Some stream ->
      Eff.tryTask (fun () -> task {
        let buf = Array.zeroCreate n
        let! read = stream.ReadAsync(buf, 0, n)
        if read = n then return buf else return buf[.. read - 1]
      })
      |> Eff.mapErr CommandIOError

  let readStderr (n: int) (child: Child) : Eff<byte array, CommandErr, 'env> =
    match child.Stderr with
    | None -> Err(CommandIOError(exn "stderr is not piped"))
    | Some stream ->
      Eff.tryTask (fun () -> task {
        let buf = Array.zeroCreate n
        let! read = stream.ReadAsync(buf, 0, n)
        if read = n then return buf else return buf[.. read - 1]
      })
      |> Eff.mapErr CommandIOError

  let readAllStdout (child: Child) : Eff<byte array, CommandErr, 'env> =
    match child.Stdout with
    | None -> Err(CommandIOError(exn "stdout is not piped"))
    | Some stream ->
      Eff.tryTask (fun () -> task {
        use ms = new System.IO.MemoryStream()
        do! stream.CopyToAsync(ms)
        return ms.ToArray()
      })
      |> Eff.mapErr CommandIOError

  let readAllStderr (child: Child) : Eff<byte array, CommandErr, 'env> =
    match child.Stderr with
    | None -> Err(CommandIOError(exn "stderr is not piped"))
    | Some stream ->
      Eff.tryTask (fun () -> task {
        use ms = new System.IO.MemoryStream()
        do! stream.CopyToAsync(ms)
        return ms.ToArray()
      })
      |> Eff.mapErr CommandIOError

  let wait (child: Child) = child.Wait()
  let kill (child: Child) = child.Kill()
  let pid (child: Child) = child.Pid
  let stdin (child: Child) = child.Stdin
  let stdout (child: Child) = child.Stdout
  let stderr (child: Child) = child.Stderr

[<Effect(Mode.Wrap)>]
type Command =
  abstract spawn: Cmd -> Eff<Child, CommandErr, unit>

type CommandProvider internal () =
  let platform =
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
      OSPlatform.Windows
    else
      OSPlatform.Linux

  let mapErr = CommandErr.ofExn platform

  let toStartInfo (cmd: Cmd) =
    let si = ProcessStartInfo(cmd.Program)
    si.UseShellExecute <- false

    for arg in cmd.Args do
      si.ArgumentList.Add(arg)

    if cmd.ClearEnv then
      si.Environment.Clear()

    for key, value in cmd.EnvVars do
      si.Environment[key] <- value

    cmd.WorkDir
    |> Option.iter (fun p -> si.WorkingDirectory <- Path.toString p)

    si.RedirectStandardInput <-
      match cmd.Stdin with
      | Piped
      | FromChild _
      | FromCmd _
      | Null -> true
      | Inherit -> false

    si.RedirectStandardOutput <-
      match cmd.Stdout with
      | Piped
      | Null -> true
      | Inherit -> false
      | FromChild _
      | FromCmd _ -> false

    si.RedirectStandardError <-
      match cmd.Stderr with
      | Piped
      | Null -> true
      | Inherit -> false
      | FromChild _
      | FromCmd _ -> false

    si

  let drainToNull (stream: System.IO.Stream) =
    stream.CopyToAsync System.IO.Stream.Null |> ignore

  let rec startAndConfigure (cmd: Cmd) : Process =
    let proc = new Process(StartInfo = toStartInfo cmd)

    if not (proc.Start()) then
      failwith $"failed to start process: {cmd.Program}"

    if cmd.Stdin = Null then
      proc.StandardInput.Close()

    if cmd.Stdout = Null then
      drainToNull proc.StandardOutput.BaseStream

    if cmd.Stderr = Null then
      drainToNull proc.StandardError.BaseStream

    match cmd.Stdin with
    | FromChild source ->
      match source.Stdout with
      | Some sourceStream ->
        sourceStream
          .CopyToAsync(proc.StandardInput.BaseStream)
          .ContinueWith(fun _ -> proc.StandardInput.Close())
        |> ignore
      | None -> proc.StandardInput.Close()
    | FromCmd sourceCmd ->
      let sourceProc = startAndConfigure sourceCmd
      sourceProc.StandardOutput.BaseStream
        .CopyToAsync(proc.StandardInput.BaseStream)
        .ContinueWith(fun _ -> proc.StandardInput.Close())
      |> ignore
    | _ -> ()

    proc

  let makeChild (proc: Process) (cmd: Cmd) : Child =
    { new Child with
        member _.Pid = proc.Id

        member _.Stdin =
          if cmd.Stdin = Piped then
            Some proc.StandardInput.BaseStream
          else
            None

        member _.Stdout =
          if cmd.Stdout = Piped then
            Some proc.StandardOutput.BaseStream
          else
            None

        member _.Stderr =
          if cmd.Stderr = Piped then
            Some proc.StandardError.BaseStream
          else
            None

        member _.Wait() =
          Eff.tryTask (fun () -> task {
            do! proc.WaitForExitAsync()
            return proc.ExitCode
          })
          |> Eff.mapErr mapErr

        member _.Kill() =
          Eff.tryCatch (fun () ->
            if not proc.HasExited then
              proc.Kill()
          )
          |> Eff.mapErr mapErr
    }

  interface Command with
    member _.spawn(cmd: Cmd) =
      Eff.tryCatch
      <| fun () -> startAndConfigure cmd |> fun proc -> makeChild proc cmd
      |> Eff.mapErr mapErr

[<AutoOpen>]
module CommandExt =
  type Command with
    static member Provider() = CommandProvider()
