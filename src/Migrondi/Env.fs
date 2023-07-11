namespace Migrondi.Env

open Serilog
open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.Migrondi


[<Struct>]
type AppEnv(logger: ILogger, migrondi: MigrondiEnv) =

  member _.Logger: ILogger = logger

  member _.Migrondi: MigrondiService = migrondi.Migrondi

  member _.Database: DatabaseService = migrondi.Database