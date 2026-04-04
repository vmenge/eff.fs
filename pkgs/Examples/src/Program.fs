open EffFs.Core

exception One
exception Two

let doOne () : Eff<_, exn, _> = Eff.err One
let doTwo () = Eff.err Two

let x () = eff {
    do! doOne ()
    do! doTwo ()

    ()
}
