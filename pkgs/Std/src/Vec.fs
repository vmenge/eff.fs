namespace EffSharp.Std

type Vec<'a> = ResizeArray<'a>

module Vec =
  let first (vec: 'a Vec) : 'a option =
    if vec.Count > 0 then Some vec[0] else None

  let last (vec: 'a Vec) : 'a option =
    if vec.Count > 0 then Some vec[vec.Count - 1] else None

  let len (vec: 'a Vec) = vec.Count

  let item i (vec: 'a Vec) : 'a option =
    if i >= 0 && i < vec.Count then Some vec[i] else None
