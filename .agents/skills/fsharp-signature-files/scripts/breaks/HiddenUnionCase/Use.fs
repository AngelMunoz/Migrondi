// expect: FS0039
// expect-text: The value or constructor 'ValidEmail' is not defined
module Use

open Lib

let bad = ValidEmail "not an email"
