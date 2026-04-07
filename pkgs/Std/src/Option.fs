namespace EffSharp.Std

module Option =
  let reject f o = Option.filter (f >> not) o
