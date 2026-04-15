namespace EffSharp.Std

open EffSharp.Gen

[<Effect(Mode.Wrap)>]
type Random =
  abstract int: unit -> int
  abstract intRange: min: int -> max: int -> int
  abstract float: unit -> float
  abstract floatRange: min: float -> max: float -> float
  abstract bool: unit -> bool
  abstract bytes: int -> byte array
  abstract shuffle: 'a array -> 'a array
  abstract shuffleMut: 'a array -> unit

type RandomProvider internal () =
  let rng = System.Random.Shared

  interface Random with
    member _.bool() = rng.Next(2) = 1

    member _.bytes n =
      let buf = Array.zeroCreate n
      rng.NextBytes buf
      buf

    member _.float() = rng.NextDouble()
    member _.floatRange min max = rng.NextDouble() * (max - min) + min

    member _.int() = rng.Next()
    member _.intRange min max = rng.Next(min, max)

    member _.shuffle arr =
      let copy = Array.copy arr
      rng.Shuffle copy
      copy

    member _.shuffleMut arr = rng.Shuffle arr


[<AutoOpen>]
module RandomExt =
  type Random with
    static member Provider() = RandomProvider()
