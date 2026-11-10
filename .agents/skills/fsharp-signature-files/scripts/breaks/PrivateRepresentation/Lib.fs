module Lib

type Email = private { Value: string }

let create (s: string) = if s.Contains '@' then Some { Value = s } else None
