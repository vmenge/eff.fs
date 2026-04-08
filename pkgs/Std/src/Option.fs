namespace EffSharp.Std

module Option =
  let reject f o = Option.filter (f >> not) o

  let zip o1 o2 =
    match o1, o2 with
    | Some x, Some y -> Some(x, y)
    | _ -> None

  let zip3 o1 o2 o3 =
    match o1, o2, o3 with
    | Some x, Some y, Some z -> Some(x, y, z)
    | _ -> None

  let tap f o1 =
    match o1 with
    | Some v ->
      f v |> ignore
      o1
    | None -> o1

  let set v o =
    match o with
    | Some _ -> Some v
    | None -> None
