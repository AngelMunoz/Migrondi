module Lib

type Email = ValidEmail of string

let create (s: string) = if s.Contains '@' then Some(ValidEmail s) else None
