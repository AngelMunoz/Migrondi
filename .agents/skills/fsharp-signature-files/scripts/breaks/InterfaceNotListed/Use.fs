module Use

open Lib

let run () =
    use ledger = new Ledger()
    ledger.GetHashCode()
