namespace EffFs.Core.Tests

[<AutoOpen>]
module Prelude =
  module Result =
    let value r =
      match r with
      | Ok v -> v
      | Error e -> failwith $"{e}"

    let error r =
      match r with
      | Ok v -> failwith $"{v}"
      | Error e -> e
