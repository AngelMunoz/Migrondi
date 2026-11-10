// expect: FS0050
// expect: FS0193
// expect-text: Type constraint mismatch
module Lib

type Ledger =
    new: unit -> Ledger

