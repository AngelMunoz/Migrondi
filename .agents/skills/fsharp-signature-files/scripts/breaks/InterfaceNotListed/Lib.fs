module Lib

type Ledger() =
    interface System.IDisposable with
        member _.Dispose() = ()
