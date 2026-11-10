// expect: FS0039
// expect-text: The record label 'Value' is not defined
module Use

open Lib

let bad : Email = { Value = "not an email" }
