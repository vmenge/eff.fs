namespace EffSharp.Std

module ValueOption =
  let toResult r vo =
    match vo with
    | ValueSome v -> Ok v
    | ValueNone -> r

  let toResultWith f vo =
    match vo with
    | ValueSome v -> Ok v
    | ValueNone -> f ()

  let reject f vo = ValueOption.filter (f >> not) vo
