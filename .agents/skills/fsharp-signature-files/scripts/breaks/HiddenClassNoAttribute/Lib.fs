module Lib

type Entry internal (count: int) =
    member _.Count = count
