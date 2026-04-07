namespace EffSharp.Examples

open EffSharp.Core
open EffSharp.Gen
open System

[<Effect>]
type Log =
  abstract info: string -> unit

[<Effect>]
type Clock =
  abstract now: unit -> DateTime

[<Effect(Mode.Wrap)>]
type Fs =
  abstract read: string -> Eff<string, string, unit>
  abstract write: string -> byte array -> Eff<unit, string, unit>
  abstract delete: string -> Eff<unit, string, unit>
