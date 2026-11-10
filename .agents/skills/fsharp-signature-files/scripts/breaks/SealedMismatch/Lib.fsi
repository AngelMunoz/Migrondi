// expect: FS0297
// expect-text: the implementation type is not sealed
module Lib

[<Sealed>]
type Ledger =
    new: unit -> Ledger
    member Count: int
