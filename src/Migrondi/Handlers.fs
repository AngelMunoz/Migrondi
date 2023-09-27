namespace Migrondi.Handlers

open System
open System.IO

open System.Security
open Serilog
open Spectre.Console
open Spectre.Console.Rendering

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
          upContent =
            "-- Add your SQL migration code below. You can delete this line but do not delete the comments above.\n\n"
          downContent =
            "-- Add your SQL rollback code below. You can delete this line but do not delete the comment above.\n\n"
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

  let runUp (amount: int option, logger: ILogger, migrondi: MigrondiService) = 0

  let runDryUp
    (
      amount: int option,
      logger: ILogger,
      migrondi: MigrondiService
    ) =
    let migrations = migrondi.DryRunUp(?amount = amount)

    logger.Information "DRY RUN: The following migrations would be applied:"

    for migration in migrations do
      logger.Information $"{migration.name}.sql"
      logger.Information "------ START TRANSACTION ------"
      logger.Information $"{migration.upContent}"
      logger.Information "------- END TRANSACTION -------"

    logger.Information $"DRY RUN: would applied '{migrations.Count}' migrations"
    0

  let runDown (amount: int option, migrondi: MigrondiService) = 0

  let runDryDown
    (
      amount: int option,
      logger: ILogger,
      migrondi: MigrondiService
    ) =
    let migrations = migrondi.DryRunDown(?amount = amount)

    logger.Information "DRY RUN: The following migrations would be reverted:"

    for migration in migrations do
      logger.Information $"{migration.name}.sql"
      logger.Information "------ START TRANSACTION ------"
      logger.Information $"{migration.upContent}"
      logger.Information "------- END TRANSACTION -------"

    logger.Information
      $"DRY RUN: would reverted '{migrations.Count}' migrations"

    0

  let listMigrations (kind: MigrationType option, migrondi: MigrondiService) =

    let printMigrationsTable (table: Table, migrations: Migration seq) =

      table.AddColumns(
        TableColumn(Markup("[green]Name[/]")),
        TableColumn(Markup("[green]Date Created[/]"))
      )
      |> ignore

      for migration in migrations do
        let date =
          DateTimeOffset
            .FromUnixTimeMilliseconds(migration.timestamp)
            .ToLocalTime()

        table.AddRow(
          Markup($"[yellow]{migration.name}[/]"),
          Markup($"[yellow]{date.ToString()}[/]")
        )
        |> ignore

      table.ShowHeaders <- true
      AnsiConsole.Write table

    let printBothMigrationsTable
      (
        table: Table,
        migrations: MigrationStatus seq
      ) =

      table.AddColumns(
        TableColumn(Markup("[green]Status[/]")),
        TableColumn(Markup("[green]Name[/]")),
        TableColumn(Markup("[green]Date Created[/]"))
      )
      |> ignore

      for migration in migrations do
        let status =
          match migration with
          | Applied _ -> Markup("[green]Applied[/]")
          | Pending _ -> Markup("[yellow]Pending[/]")

        let date =
          DateTimeOffset
            .FromUnixTimeMilliseconds(migration.Value.timestamp)
            .ToLocalTime()

        table.AddRow(
          status,
          Markup($"[yellow]{migration.Value.name}[/]"),
          Markup($"[yellow]{date.ToString()}[/]")
        )
        |> ignore

      table.ShowHeaders <- true
      AnsiConsole.Write table

    let allMigrations = migrondi.MigrationsList()

    let table = Table()

    match kind with
    | Some MigrationType.Up ->
      let applied =
        allMigrations
        |> Seq.choose(fun m ->
          match m with
          | Applied a -> Some a
          | _ -> None
        )

      table.Title <- TableTitle("Applied Migrations")

      printMigrationsTable(table, applied)
    | Some MigrationType.Down ->
      let pending =
        allMigrations
        |> Seq.choose(fun m ->
          match m with
          | Applied _ -> None
          | Pending a -> Some a
        )

      table.Title <- TableTitle("Pending Migrations")
      printMigrationsTable(table, pending)
    | None ->
      table.Title <- TableTitle("All Migrations")
      printBothMigrationsTable(table, allMigrations)

    0

  let migrationStatus
    (
      name: string,
      logger: ILogger,
      migrondi: MigrondiService
    ) =
    let formatTimestamp timestamp =
      let date =
        DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime()

      date.ToString()

    match migrondi.ScriptStatus(name) with
    | Applied migration ->
      logger.Information
        $"Migration {migration.name} was created on '{formatTimestamp(migration.timestamp)}' and is currently applied."
    | Pending migration ->
      logger.Information
        $"Migration {migration.name} was created on '{formatTimestamp(migration.timestamp)}' and is not currently applied."

    0