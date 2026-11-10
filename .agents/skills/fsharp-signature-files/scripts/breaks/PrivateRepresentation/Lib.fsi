module Lib

type Email = private { Value: string }

val create: string -> Email option
