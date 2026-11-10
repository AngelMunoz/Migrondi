// expect: FS1093
module Use

open Lib

let bad : Email = { Value = "not an email" }
