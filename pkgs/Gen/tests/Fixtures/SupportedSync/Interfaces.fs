namespace SupportedSyncRed

open EffSharp.Gen

[<Effect>]
type ILogger =
  abstract Debug: string -> unit

[<Effect>]
type IClock =
  abstract Now: unit -> string

[<Effect>]
type IParser =
  abstract Parse: string -> Result<int, ParseError>

[<Effect>]
type ILookup =
  abstract TryFind: int * string -> Result<User, LookupError>
