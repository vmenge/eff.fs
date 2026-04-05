open EffFs.Core

let readFile filename : Eff<string, exn, _> = Pure "file contents"

let parseFile fileContents : Result<{| name: string |}, string> =
  Ok {| name = "bla" |}

let x () = eff {
  let! contents = readFile "bla"
  let! parsed = parseFile contents |> Result.mapError exn

  printfn $"{parsed}"

  ()
}

let y () =
  readFile "bla"
  |> Eff.bind (fun contents -> contents |> parseFile |> Eff.ofResultWith exn)
