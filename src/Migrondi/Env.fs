namespace Migrondi.Env

open Serilog
open Migrondi.Core.Migrondi


[<Struct>]
type AppEnv(logger: ILogger, migrondi: MigrondiService) =

  member _.Logger: ILogger = logger

  member _.Migrondi: MigrondiService = migrondi