namespace EffFs.Core

open System.Threading.Tasks

[<AutoOpen>]
module CE =
  type EffBuilderBase() =
    member _.Yield(value: 't) : Eff<'t, 'e, 'env> = Eff.value value

    member _.Return(value: 't) : Eff<'t, 'e, 'env> = Eff.value value

    member _.Zero() : Eff<unit, 'e, 'env> = Eff.value ()

    member _.Delay(f: unit -> Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> =
      Eff.suspend f

    member _.Combine
      (left: Eff<unit, 'e, 'env>, right: Eff<'t, 'e, 'env>)
      : Eff<'t, 'e, 'env> =
      Eff.bind (fun () -> right) left

    member _.Using
      (resource: 'r, binder: 'r -> Eff<'t, 'e, 'env>)
      : Eff<'t, 'e, 'env> when 'r :> System.IDisposable =
      Eff.bracket
        (Eff.value resource)
        (fun r -> Eff.thunk (fun () -> r.Dispose()))
        binder

    member this.While
      (guard: unit -> bool, body: Eff<unit, 'e, 'env>)
      : Eff<unit, 'e, 'env> =
      if not (guard ()) then
        Eff.value ()
      else
        Eff.bind (fun () -> this.While(guard, body)) body

    member _.For
      (sequence: seq<'t>, body: 't -> Eff<unit, 'e, 'env>)
      : Eff<unit, 'e, 'env> =
      Eff.suspend (fun () ->
        let enumerator = sequence.GetEnumerator()

        let cleanup = Eff.thunk (fun () -> enumerator.Dispose())

        let rec loop () =
          Eff.suspend (fun () ->
            if enumerator.MoveNext() then
              Eff.bind (fun () -> loop ()) (body enumerator.Current)
            else
              Eff.value ()
          )

        Eff.defer cleanup (loop ())
      )

    [<CustomOperation("defer", MaintainsVariableSpaceUsingBind = true)>]
    member _.Defer
      (
        state: Eff<'t, 'e, 'env>,
        [<ProjectionParameter>] cleanup: 't -> Eff<unit, 'e, 'env>
      ) : Eff<'t, 'e, 'env> =
      state
      |> Eff.bind (fun vspace -> Eff.defer (cleanup vspace) (Eff.value vspace))

    [<CustomOperation("defer", MaintainsVariableSpaceUsingBind = true)>]
    member _.Defer
      (
        state: Eff<'t, 'e, 'env>,
        [<ProjectionParameter>] cleanup: 't -> unit -> unit
      ) : Eff<'t, 'e, 'env> =
      state
      |> Eff.bind (fun vspace ->
        Eff.defer (Eff.thunk (cleanup vspace)) (Eff.value vspace)
      )

    member _.Source(sequence: seq<'t>) : seq<'t> = sequence



  type EffBuilder() =
    inherit EffBuilderBase()

    member _.ReturnFrom(eff: Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> = eff

    member _.Bind
      (eff: Eff<'t, 'e, 'env>, f: 't -> Eff<'u, 'e, 'env>)
      : Eff<'u, 'e, 'env> =
      Eff.bind f eff

    member _.BindReturn
      (eff: Eff<'t, 'e, 'env>, f: 't -> 'u)
      : Eff<'u, 'e, 'env> =
      Eff.map f eff

    member _.Source(eff: Eff<'t, 'e, 'env>) : Eff<'t, 'e, 'env> = eff



  [<AutoOpen>]
  module CEExtLowPriority =
    type EffBuilder with
      member _.Source(valueTask: ValueTask<'t>) : Eff<'t, 'e, 'env> =
        Eff.ofValueTask (fun () -> valueTask)

      member _.Source(task: Task<'t>) : Eff<'t, 'e, 'env> =
        Eff.ofTask (fun () -> task)

      member _.Source(async: Async<'t>) : Eff<'t, 'e, 'env> =
        Eff.ofAsync (fun () -> async)

  [<AutoOpen>]
  module CEExtHighPriority =
    type EffBuilder with
      member _.Source
        (valueTaskResult: ValueTask<Result<'t, 'e>>)
        : Eff<'t, 'e, 'env> =
        Eff.ofValueTask (fun () -> valueTaskResult) |> Eff.bind Eff.ofResult

      member _.Source(taskResult: Task<Result<'t, 'e>>) : Eff<'t, 'e, 'env> =
        Eff.ofTask (fun () -> taskResult) |> Eff.bind Eff.ofResult

      member _.Source(asyncResult: Async<Result<'t, 'e>>) : Eff<'t, 'e, 'env> =
        Eff.ofAsync (fun () -> asyncResult) |> Eff.bind Eff.ofResult

      member _.Source(result: Result<'t, 'e>) : Eff<'t, 'e, 'env> =
        Eff.ofResult result

      member _.Source(option: Option<'t>) : Eff<'t, exn, 'env> =
        Eff.ofOption option

      member _.Source(valueOption: ValueOption<'t>) : Eff<'t, exn, 'env> =
        Eff.ofValueOption valueOption

  let eff = EffBuilder()
