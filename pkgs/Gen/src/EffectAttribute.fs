namespace EffSharp.Gen

open System

type Mode =
  | Direct = 0
  | Wrap = 1

[<AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)>]
type EffectAttribute(mode: Mode) =
  inherit Attribute()

  new() = EffectAttribute(Mode.Direct)

  member _.Mode = mode
