namespace Migrondi.Handlers

open System
open System.IO

open System.Security
open Microsoft.Extensions.Logging
open Spectre.Console

open Migrondi.Core
open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem

[<RequireQualifiedAccess>]
module internal Init =
  let handler (path: DirectoryInfo, fs: IMiFileSystem, logger: ILogger) =
    logger.LogInformation(
      "Initializing a new migrondi project at: {PathName}.",
      path.FullName
    )

    let configPath = Path.Combine(path.FullName, "./migrondi.json")
    let config = MigrondiConfig.Default
    fs.WriteConfiguration(config, configPath)
    let subpath = path.CreateSubdirectory(config.migrations)

    logger.LogInformation(
      "migrondi.json and {MigrationsDirectory} directory created successfully.",
      subpath.Name
    )

    0


[<RequireQualifiedAccess>]
module internal Migrations =

  let newMigration
    (name: string, manualTransaction: bool option, logger: ILogger, migrondi: IMigrondi)
    =
    logger.LogInformation(
      "Creating a new migration with name: {MigrationName}.",
      name
    )

    try
      let migration = migrondi.RunNew(name, ?manualTransaction = manualTransaction)

      logger.LogInformation(
        "Migration {MigrationName} created successfully.",
        migration.name
      )

      0
    with
    | :? IOException as e ->
      logger.LogError(
        "There was a problem when writing the migration file: '{Message}'",
        e.Message
      )

      1
    | :? SecurityException as e ->
      logger.LogError(
        "The user does not have permissions on this directory/file, please check the permissions and try again.\n{Message}",
        e.Message
      )

      1

  let runUp (amount: int option, logger: ILogger, migrondi: IMigrondi) =

    try
      let appliedMigrations = migrondi.RunUp(?amount = amount)

      for migration in appliedMigrations do
        logger.LogInformation(
          "Applied migration '{MigrationName}' successfully.",
          migration.name
        )

      0
    with MigrationApplicationFailed migration ->
      logger.LogError(
        "Failed to apply migration '{MigrationName}'.",
        migration.name
      )

      1


  let runDryUp (amount: int option, logger: ILogger, migrondi: IMigrondi) =
    let migrations = migrondi.DryRunUp(?amount = amount)

    logger.LogInformation "DRY RUN: The following migrations would be applied:"

    for migration in migrations do
      logger.LogInformation(
        "{MigrationName}.sql\n------ START TRANSACTION ------\n{MigrationContent}\n------- END TRANSACTION -------",
        migration.name,
        migration.upContent
      )

    logger.LogInformation
      $"DRY RUN: would applied '{migrations.Count}' migrations"

    0

  let runDown (amount: int option, logger: ILogger, migrondi: IMigrondi) =

    try
      let reverted = migrondi.RunDown(?amount = amount)

      for migration in reverted do
        logger.LogInformation(
          "Reverted migration '{MigrationName}' successfully.",
          migration.name
        )

      0
    with MigrationApplicationFailed migration ->
      logger.LogError(
        "Failed to apply migration '{MigrationName}'.",
        migration.name
      )

      1

  let runDryDown (amount: int option, logger: ILogger, migrondi: IMigrondi) =
    let migrations = migrondi.DryRunDown(?amount = amount)

    logger.LogInformation "DRY RUN: The following migrations would be reverted:"

    for migration in migrations do
      logger.LogInformation(
        "{MigrationName}\n------ START TRANSACTION ------\n{MigrationContent}\n------- END TRANSACTION -------",
        migration.name,
        migration.upContent
      )

    logger.LogInformation(
      "DRY RUN: would reverted '{MigrationCount}' migrations",
      migrations.Count
    )

    0

  let listMigrations
    (
      useJson: bool,
      logger: ILogger,
      serializer: IMiMigrationSerializer,
      kind: MigrationType option,
      migrondi: IMigrondi
    ) =

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
      (table: Table, migrations: MigrationStatus seq)
      =

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

    let printJson (migrations: Migration seq, status: string) =
      let encoded = migrations |> Seq.map(fun m -> serializer.EncodeJson m)

      logger.LogInformation(
        "Listing {Status} migrations: {Migrations}",
        status,
        encoded
      )

    let printJsonBoth (migrations: MigrationStatus seq) =
      let encoded =
        migrations
        |> Seq.map(fun status ->
          match status with
          | Applied m -> ("Applied", serializer.EncodeJson m)
          | Pending m -> ("Pending", serializer.EncodeJson m)
        )

      logger.LogInformation("Listing migrations: {Migrations}", encoded)

    let allMigrations = migrondi.MigrationsList()

    match kind with
    | Some MigrationType.Up ->
      let applied =
        allMigrations
        |> Seq.choose(fun m ->
          match m with
          | Applied a -> Some a
          | _ -> None
        )

      if useJson then
        printJson(applied, "Applied")
      else
        let table = Table()
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

      if useJson then
        printJson(pending, "Pending")
      else
        let table = Table()
        table.Title <- TableTitle("Pending Migrations")
        printMigrationsTable(table, pending)
    | None ->
      if useJson then
        printJsonBoth(allMigrations)
      else
        let table = Table()
        table.Title <- TableTitle("All Migrations")
        printBothMigrationsTable(table, allMigrations)

    0

  let migrationStatus (name: string, logger: ILogger, migrondi: IMigrondi) =
    let formatTimestamp timestamp =
      let date =
        DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToLocalTime()

      date.ToString()

    match migrondi.ScriptStatus(name) with
    | Applied migration ->
      logger.LogInformation(
        "Migration {migration.name} was created on '{Timestamp}' and is currently applied.",
        formatTimestamp(migration.timestamp)
      )
    | Pending migration ->
      logger.LogInformation(
        "Migration {migration.name} was created on '{Timestamp}' and is not currently applied.",
        formatTimestamp(migration.timestamp)
      )

    0