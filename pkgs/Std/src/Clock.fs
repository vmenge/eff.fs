namespace EffSharp.Std

open EffSharp.Gen
open System
open System.Diagnostics

[<Struct>]
type Instant =
  private
  | Instant of int64

  static member (-)(Instant a, Instant b) : TimeSpan =
    Stopwatch.GetElapsedTime(b, a)

  static member (+)(Instant ticks, ts: TimeSpan) : Instant =
    Instant(ticks + int64 (ts.TotalSeconds * float Stopwatch.Frequency))

  static member (-)(Instant ticks, ts: TimeSpan) : Instant =
    Instant(ticks - int64 (ts.TotalSeconds * float Stopwatch.Frequency))

module Instant =
  let elapsed (Instant start) = Stopwatch.GetElapsedTime(start)

[<Effect(Mode.Wrap)>]
type Clock =
  abstract now: unit -> DateTime
  abstract utcNow: unit -> DateTime
  abstract instant: unit -> Instant

type ClockProvider internal () =
  interface Clock with
    member _.instant() = Instant(Stopwatch.GetTimestamp())
    member _.now() : DateTime = DateTime.Now
    member _.utcNow() : DateTime = DateTime.UtcNow

[<AutoOpen>]
module ClockExt =
  type Clock with
    static member Provider() = ClockProvider()
