namespace Migrondi.Handlers

open System
open System.IO

open Migrondi.Core
open Migrondi.Core.Migrondi

[<RequireQualifiedAccess>]
module Init =
  let handler (path: DirectoryInfo, migrondi: MigrondiService) = 0

[<RequireQualifiedAccess>]
module Migrations =

  let newMigration (name: string, migrondi: MigrondiService) = 0

  let runUp (amount: int option, migrondi: MigrondiService) = 0

  let runDryUp (amount: int option, migrondi: MigrondiService) = 0

  let runDown (amount: int option, migrondi: MigrondiService) = 0

  let runDryDown (amount: int option, migrondi: MigrondiService) = 0

  let listMigrations (kind: MigrationType, migrondi: MigrondiService) = 0

  let migrationStatus (name: string, migrondi: MigrondiService) = 0