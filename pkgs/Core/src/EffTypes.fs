namespace EffSharp.Core

open System.Threading.Tasks

[<Struct; RequireQualifiedAccess>]
type Eff<'t, 'e, 'env> =
  internal
  | Pure of value: 't
  | Err of err: 'e
  | Crash of exn: exn
  | Suspend of suspend: (unit -> Eff<'t, 'e, 'env>)
  | Thunk of thunk: (unit -> 't)
  | Task of tsk: (unit -> Task<'t>)
  | Read of read: ('env -> 't)
  | Node of Node<'t, 'e, 'env>

and [<AbstractClass>] internal Node<'t, 'e, 'env>() = class end

type Fiber<'t, 'e> internal (handle: obj) =
  member internal _.Handle = handle

[<AutoOpen>]
module Constructors =
  let Pure v = Eff.Pure v
  let Err e = Eff.Err e

[<RequireQualifiedAccess>]
type Exit<'t, 'e> =
  | Ok of 't
  | Err of 'e
  | Aborted
  | Exn of exn

type TimeoutResult<'t> =
  | Completed of 't
  | TimedOut

module Exit =
  let isOk rr =
    match rr with
    | Exit.Ok _ -> true
    | _ -> false

  let ok rr =
    match rr with
    | Exit.Ok v -> v
    | Exit.Err e -> failwith $"{e}"
    | Exit.Aborted -> failwith "aborted"
    | Exit.Exn e -> raise e

  let err rr =
    match rr with
    | Exit.Ok v -> failwith $"{v}"
    | Exit.Err e -> e
    | Exit.Aborted -> failwith "aborted"
    | Exit.Exn e -> raise e

  let ex rr =
    match rr with
    | Exit.Ok v -> failwith $"{v}"
    | Exit.Err e -> failwith $"{e}"
    | Exit.Aborted -> failwith "aborted"
    | Exit.Exn e -> e
