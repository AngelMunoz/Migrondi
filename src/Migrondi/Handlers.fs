namespace Migrondi.Handlers

open System
open System.IO

open System.Security
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

  let newMigration (name: string, logger: #ILogger, fs: #FileSystemService) =
    logger.Information $"Creating a new migration with name: {name}."
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    let name = $"{name}_{timestamp}.sql"

    try
      fs.WriteMigration(
        {
          name = name
          timestamp = timestamp
          upContent = ""
          downContent = ""
        },
        name
      )

      logger.Information $"Migration {name} created successfully."
      0
    with
    | :? IOException as e ->
      logger.Error(
        "There was a problem when writing the migration file: '{Message}'",
        e.Message
      )

      1
    | :? SecurityException as e ->
      logger.Error(
        "The user does not have permissions on this directory/file, please check the permissions and try again.\n{Message}",
        e.Message
      )

      1

  let runUp (amount: int option, migrondi: MigrondiService) = 0

  let runDryUp (amount: int option, migrondi: MigrondiService) = 0

  let runDown (amount: int option, migrondi: MigrondiService) = 0

  let runDryDown (amount: int option, migrondi: MigrondiService) = 0

  let listMigrations (kind: MigrationType, migrondi: MigrondiService) = 0

  let migrationStatus (name: string, migrondi: MigrondiService) = 0