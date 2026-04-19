namespace EffSharp.Examples

open EffSharp.Core
open EffSharp.Gen
open System

[<Effect>]
type DbusService =
  abstract GetResult :  int -> int
