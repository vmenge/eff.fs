namespace WrappedNamingRed

open EffSharp.Gen

[<Effect(Mode.Wrap)>]
type IGreeter =
  abstract Greet: string -> string

[<Effect(Mode.Wrap)>]
type Logger =
  abstract Debug: string -> unit

[<Effect(Mode.Wrap)>]
type Ilogger =
  abstract Trace: string -> unit
