namespace Migrondi.Handlers

open System
open System.IO

open Serilog

open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Migrondi

[<RequireQualifiedAccess>]
module Init =
  let handler (path: DirectoryInfo, fs: #FileSystemService, logger: #ILogger) =
    logger.Information
      $"Initializing a new migrondi project at: {path.FullName} ."

    let configPath = Path.Combine(path.FullName, "./migrondi.json")
    let config = MigrondiConfig.Default
    fs.WriteConfiguration(config, configPath)
    let subpath = path.CreateSubdirectory(config.migrations)

    logger.Information
      $"migrondi.json and {subpath.Name} directory created successfully."

    0


[<RequireQualifiedAccess>]
module Migrations =

  let newMigration (name: string, migrondi: MigrondiService) = 0

  let runUp (amount: int option, migrondi: MigrondiService) = 0

  let runDryUp (amount: int option, migrondi: MigrondiService) = 0

  let runDown (amount: int option, migrondi: MigrondiService) = 0

  let runDryDown (amount: int option, migrondi: MigrondiService) = 0

  let listMigrations (kind: MigrationType, migrondi: MigrondiService) = 0

  let migrationStatus (name: string, migrondi: MigrondiService) = 0