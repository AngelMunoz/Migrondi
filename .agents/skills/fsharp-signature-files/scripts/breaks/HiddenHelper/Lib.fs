module Lib

let normalize (s: string) = s.Trim()

let create (s: string) = if s.Contains '@' then Some(normalize s) else None
