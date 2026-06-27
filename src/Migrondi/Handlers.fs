namespace Migrondi.Handlers

open System
open System.IO
open System.Text.Json

open System.Security
open Microsoft.Extensions.Logging
open Spectre.Console

open Migrondi.Core
open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem
open FsToolkit.ErrorHandling

[<RequireQualifiedAccess>]
module internal Init =

  type private LoadingFileError =
    | FileNotFound
    | FoundButUnparsable

  let private recoverConfig(values: Map<string, string>) : MigrondiConfig =
    let defaults = MigrondiConfig.Default

    let driver =
      values
      |> Map.tryFind "driver"
      |> Option.bind(fun value ->
        try
          MigrondiDriver.FromString value |> Some
        with _ ->
          None)
      |> Option.defaultValue defaults.driver

    {
      connection =
        values
        |> Map.tryFind "connection"
        |> Option.defaultValue defaults.connection
      migrations =
        values
        |> Map.tryFind "migrations"
        |> Option.defaultValue defaults.migrations
      tableName =
        values
        |> Map.tryFind "tableName"
        |> Option.defaultValue defaults.tableName
      driver = driver
    }

  let private (|HardStop|MergeUnforced|ForceClean|ForceMerge|CleanSetup|)
    (isEmpty, force, merge)
    =
    match isEmpty, force, merge with
    | _, false, true -> MergeUnforced
    | false, true, true -> ForceMerge
    | true, _, false -> CleanSetup
    | false, true, false -> ForceClean
    | true, _, _ -> ForceClean
    | false, false, _ -> HardStop

  let private isDirectoryEmpty(path: DirectoryInfo) =
    try
      let noDirs =
        Directory.EnumerateDirectories(
          path.FullName,
          "*",
          SearchOption.AllDirectories
        )
        |> Seq.isEmpty

      let noFiles =
        Directory.EnumerateFiles(
          path.FullName,
          "*",
          SearchOption.AllDirectories
        )
        |> Seq.isEmpty

      noDirs && noFiles
    with :? DirectoryNotFoundException ->
      path.Create()
      true

  let private loadConfig
    (path: DirectoryInfo)
    (configPath: string)
    (serializer: IMiConfigurationSerializer)
    (logger: ILogger)
    : Result<MigrondiConfig, LoadingFileError> =
    let textContent =
      try
        File.ReadAllText configPath |> Ok
      with
      | :? DirectoryNotFoundException ->
        path.Create()
        Error FileNotFound
      | :? FileNotFoundException -> Error FileNotFound

    try
      textContent
      |> Result.map serializer.Decode
      |> Result.tee(fun _ ->
        logger.LogInformation("Found migrondi.json at {Path}", path.FullName))
    with :? DeserializationFailed ->
      try
        textContent
        |> Result.tee(fun _ ->
          logger.LogWarning(
            "Found an invalid migrondi.json file at {Path}",
            path.FullName
          ))
        |> Result.map(
          JsonSerializer.Deserialize<Map<string, string>> >> recoverConfig
        )
      with :? JsonException ->
        logger.LogWarning "migrondi.json file is not valid json"
        Error FoundButUnparsable

  let private migrationsDirectory
    (path: DirectoryInfo)
    (config: MigrondiConfig)
    =
    if
      Path.IsPathRooted config.migrations
      || Path.IsPathFullyQualified config.migrations
    then
      DirectoryInfo config.migrations
    else
      path.CreateSubdirectory config.migrations

  let private hasExistingMigrations
    (path: DirectoryInfo)
    (config: MigrondiConfig)
    =
    let dir = migrationsDirectory path config

    let files =
      try
        dir.EnumerateFiles(
          "*.sql",
          EnumerationOptions(
            MatchCasing = MatchCasing.CaseInsensitive,
            RecurseSubdirectories = false
          )
        )
      with :? DirectoryNotFoundException ->
        dir.Create()
        Seq.empty

    files |> Seq.isEmpty |> not

  let private runForceMerge
    (path: DirectoryInfo)
    (fs: IMiFileSystem)
    (configPath: string)
    (serializer: IMiConfigurationSerializer)
    (logger: ILogger)
    =
    let config =
      loadConfig path configPath serializer logger
      |> Result.defaultValue MigrondiConfig.Default

    logger.LogInformation(
      "Initializing a migrondi project at {PathName}",
      path.FullName
    )

    fs.WriteConfiguration(config, configPath)

    if hasExistingMigrations path config then
      logger.LogWarning(
        "Sql Files already existing in {MigrationsDir}, leaving untouched.",
        config.migrations
      )

    logger.LogInformation "Migrondi project merged and ready to work."
    0

  let private runCleanSetup
    (path: DirectoryInfo)
    (fs: IMiFileSystem)
    (configPath: string)
    (logger: ILogger)
    =
    logger.LogInformation(
      "Initializing a new migrondi project at: {PathName}.",
      path.FullName
    )

    let config = MigrondiConfig.Default
    let migrationsDirPath = Path.Combine(path.FullName, config.migrations)

    try
      Directory.Delete(migrationsDirPath, true)
    with
    | :? IOException
    | :? UnauthorizedAccessException ->
      logger.LogWarning(
        "Unable to delete the migrations directory at {MigrationsDir}",
        migrationsDirPath
      )

    path.Create()

    fs.WriteConfiguration(config, configPath)

    let subpath = path.CreateSubdirectory config.migrations

    logger.LogInformation(
      "migrondi.json and {MigrationsDirectory} directory created successfully.",
      subpath.Name
    )

    0

  let handler
    (
      path: DirectoryInfo,
      fs: IMiFileSystem,
      serializer: IMiConfigurationSerializer,
      logger: ILogger,
      force: bool option,
      merge: bool option
    ) =
    let force = defaultArg force false
    let merge = defaultArg merge false
    let configPath = Path.Combine(path.FullName, "./migrondi.json")

    match (isDirectoryEmpty path, force, merge) with
    | ForceMerge -> runForceMerge path fs configPath serializer logger
    | CleanSetup
    | ForceClean -> runCleanSetup path fs configPath logger
    | MergeUnforced ->
      logger.LogError "--merge can only be used together with --force."
      1
    | HardStop ->
      logger.LogError(
        "The Directory {ConfigPath} is not empty. Use --force to overwrite it, or --force --merge to adopt its current values.",
        path.FullName
      )

      1


[<RequireQualifiedAccess>]
module internal Migrations =

  let newMigration
    (
      name: string,
      manualTransaction: bool option,
      logger: ILogger,
      migrondi: IMigrondi
    ) =
    logger.LogInformation(
      "Creating a new migration with name: {MigrationName}.",
      name
    )

    try
      let migration =
        migrondi.RunNew(name, ?manualTransaction = manualTransaction)

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

  let runUp(amount: int option, logger: ILogger, migrondi: IMigrondi) =

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


  let runDryUp(amount: int option, logger: ILogger, migrondi: IMigrondi) =
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

  let runDown(amount: int option, logger: ILogger, migrondi: IMigrondi) =

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

  let runDryDown(amount: int option, logger: ILogger, migrondi: IMigrondi) =
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
      kind: MigrationType option,
      migrondi: IMigrondi
    ) =

    let printMigrationsTable(table: Table, migrations: Migration seq) =

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

    let printJson(migrations: Migration seq, status: string) =
      let data =
        migrations
        |> Seq.map(fun m -> {|
          name = m.name
          timestamp = m.timestamp
          upContent = m.upContent
          downContent = m.downContent
          manualTransaction = m.manualTransaction
        |})

      logger.LogInformation(
        "Listing {Status} migrations: {@Migrations}",
        status,
        data
      )

    let printJsonBoth(migrations: MigrationStatus seq) =
      let data =
        migrations
        |> Seq.map(fun status ->
          let tag, m =
            match status with
            | Applied m -> "Applied", m
            | Pending m -> "Pending", m

          {|
            status = tag
            name = m.name
            timestamp = m.timestamp
            upContent = m.upContent
            downContent = m.downContent
            manualTransaction = m.manualTransaction
          |})

      logger.LogInformation("Listing migrations: {@Migrations}", data)

    let allMigrations = migrondi.MigrationsList()

    match kind with
    | Some MigrationType.Up ->
      let applied =
        allMigrations
        |> Seq.choose(fun m ->
          match m with
          | Applied a -> Some a
          | _ -> None)

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
          | Pending a -> Some a)

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

  let migrationStatus(name: string, logger: ILogger, migrondi: IMigrondi) =
    let fileName =
      if name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) then
        name
      else
        $"{name}.sql"

    try
      match migrondi.ScriptStatus fileName with
      | Applied migration ->
        logger.LogInformation(
          "Migration {MigrationName} (timestamp {Timestamp}) is {Status}.",
          migration.name,
          migration.timestamp,
          "Applied"
        )
      | Pending migration ->
        logger.LogInformation(
          "Migration {MigrationName} (timestamp {Timestamp}) is {Status}.",
          migration.name,
          migration.timestamp,
          "Pending"
        )

      0
    with :? SourceNotFound ->
      logger.LogWarning(
        "No migration found with name {MigrationName} ({Status}).",
        name,
        "NotFound"
      )

      0
