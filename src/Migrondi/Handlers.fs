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

  /// Raw content sources for a new migration, as given on the command line.
  type NewContentArgs = {
    Up: string option
    Down: string option
    UpFile: string option
    DownFile: string option
  }

  /// Content resolved from NewContentArgs; a section is absent when
  /// neither its inline nor its file source was given.
  type ResolvedNewContent = {
    UpContent: string option
    DownContent: string option
  }

  type NewMigrationArgs = {
    Name: string
    ManualTransaction: bool option
    Content: NewContentArgs
    Stdin: TextReader
  }

  /// Resolves one migration section from its inline or file source.
  /// `-` as the file path reads the section from stdin; stdin can only
  /// feed one section because it can be consumed only once.
  let resolveNewContent
    (stdin: TextReader, args: NewContentArgs)
    : Result<ResolvedNewContent, string> =
    let tryRead(label: string, source: string, read: unit -> string) =
      try
        Ok(Some(read()))
      with
      | :? IOException as ex ->
        Error $"Could not read the {label} from '{source}': {ex.Message}"
      | :? UnauthorizedAccessException as ex ->
        Error $"Could not read the {label} from '{source}': {ex.Message}"
      | :? SecurityException as ex ->
        Error $"Could not read the {label} from '{source}': {ex.Message}"
      | :? ArgumentException as ex ->
        Error $"Could not read the {label} from '{source}': {ex.Message}"
      | :? NotSupportedException as ex ->
        Error $"Could not read the {label} from '{source}': {ex.Message}"

    let resolveSection
      (label: string, inlineContent: string option, file: string option)
      =
      match inlineContent, file with
      | Some _, Some _ ->
        Error
          $"Use either --{label} or --{label}-file for the {label} section, not both."
      | Some content, None -> Ok(Some content)
      | None, Some "-" -> tryRead(label, "stdin", stdin.ReadToEnd)
      | None, Some path -> tryRead(label, path, fun () -> File.ReadAllText path)
      | None, None -> Ok None

    match args.UpFile, args.DownFile with
    | Some "-", Some "-" ->
      Error "stdin ('-') can only be used for one of --up-file or --down-file."
    | _ -> result {
        let! upContent = resolveSection("up", args.Up, args.UpFile)
        let! downContent = resolveSection("down", args.Down, args.DownFile)

        return {
          UpContent = upContent
          DownContent = downContent
        }
      }

  let newMigration
    (logger: ILogger, migrondi: IMigrondi, args: NewMigrationArgs)
    =
    logger.LogInformation(
      "Creating a new migration with name: {MigrationName}.",
      args.Name
    )

    match resolveNewContent(args.Stdin, args.Content) with
    | Error message ->
      logger.LogError("{Message}", message)
      1
    | Ok resolved ->

      try
        let migration =
          migrondi.RunNew(
            args.Name,
            ?upContent = resolved.UpContent,
            ?downContent = resolved.DownContent,
            ?manualTransaction = args.ManualTransaction
          )

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

  type ListFilters = {
    Kind: MigrationType option
    Pending: bool option
    Applied: bool option
    Limit: int option
    Last: int option
    Search: string option
    ShowPath: bool
    ShowContent: bool
  }

  type ListArgs = {
    UseJson: bool
    MigrationsDir: string
    Filters: ListFilters
  }

  type StatusSelection =
    | AllMigrations
    | AppliedOnly
    | PendingOnly

  /// Resolves the status flags to one selection, rejecting combinations
  /// that contradict each other.
  let statusSelection(filters: ListFilters) : Result<StatusSelection, string> =
    let wantsPending = filters.Pending = Some true
    let wantsApplied = filters.Applied = Some true

    match filters.Kind, wantsPending, wantsApplied with
    | _, true, true -> Error "--pending and --applied cannot be used together."
    | Some MigrationType.Up, true, _ ->
      Error "--kind up lists applied migrations and conflicts with --pending."
    | Some MigrationType.Down, _, true ->
      Error "--kind down lists pending migrations and conflicts with --applied."
    | Some MigrationType.Up, _, _
    | _, _, true -> Ok AppliedOnly
    | Some MigrationType.Down, _, _
    | _, true, _ -> Ok PendingOnly
    | None, false, false -> Ok AllMigrations

  /// Applies status, search, and count filters to the migrations, in the
  /// order returned by the core (newest first). `--limit` keeps the first
  /// entries of that order; `--last` keeps the final entries.
  let filterMigrations
    (filters: ListFilters, migrations: MigrationStatus seq)
    : Result<MigrationStatus list, string> =
    let matchesSearch (text: string) (status: MigrationStatus) =
      let migration = status.Value

      [ migration.name; migration.upContent; migration.downContent ]
      |> List.exists(fun value ->
        value.Contains(text, StringComparison.OrdinalIgnoreCase))

    result {
      let! selection = statusSelection filters

      let byStatus =
        migrations
        |> Seq.filter(fun status ->
          match selection, status with
          | AppliedOnly, Pending _
          | PendingOnly, Applied _ -> false
          | _ -> true)
        |> List.ofSeq

      let searched =
        match filters.Search with
        | Some text -> byStatus |> List.filter(matchesSearch text)
        | None -> byStatus

      match filters.Limit, filters.Last with
      | Some _, Some _ ->
        return! Error "--limit and --last cannot be used together."
      | Some limit, None ->
        if limit <= 0 then
          return! Error "--limit must be a positive number."
        else
          return searched |> List.truncate limit
      | None, Some last ->
        if last <= 0 then
          return! Error "--last must be a positive number."
        else
          return searched |> List.skip(max 0 (searched.Length - last))
      | None, None -> return searched
    }

  /// Reconstructs the migration file name from the decoded name, which
  /// may already carry the extension, the V1 timestamp prefix, only the
  /// V0 timestamp suffix, or neither.
  let migrationFileName(migration: Migration) =
    let name = migration.name
    let timestamp = migration.timestamp.ToString()

    match name with
    | _ when name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) -> name
    | _ when name.StartsWith($"{timestamp}_") -> $"{name}.sql"
    | _ when name.EndsWith($"_{timestamp}") -> $"{name}.sql"
    | _ -> $"{timestamp}_{name}.sql"

  type TargetRunArgs = { Target: string; DryRun: bool }

  /// Resolves a `--to` target against the known migrations. The target
  /// may be the friendly name, the file name with or without `.sql`, or
  /// the timestamp, matching how agents identify a migration from
  /// `list` output.
  let resolveTarget(migrations: MigrationStatus seq, target: string) =
    let normalized =
      if target.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) then
        target.Substring(0, target.Length - 4)
      else
        target

    let targetKeys(status: MigrationStatus) =
      let migration = status.Value
      let fileName = migrationFileName migration

      let stem = fileName.Substring(0, fileName.Length - 4)

      let friendly =
        match Migration.ExtractFromFilename fileName with
        | Ok(name, _) -> name
        | Error _ -> migration.name

      [
        migration.name
        fileName
        stem
        friendly
        migration.timestamp.ToString()
      ]

    let matches =
      migrations
      |> Seq.filter(fun status -> targetKeys status |> List.contains normalized)
      |> List.ofSeq

    match matches with
    | [] -> Error $"No migration found matching '{target}'."
    | [ status ] -> Ok status
    | _ ->
      let names =
        matches
        |> List.map(fun status -> status.Value.name)
        |> String.concat ", "

      Error $"Target '{target}' matches more than one migration: {names}."

  /// Amount for `up --to`: pending migrations are applied oldest first,
  /// so the amount runs through the target, inclusive. An applied target
  /// is already reached and runs nothing.
  let upTargetAmount(target: MigrationStatus, pending: Migration seq) =
    match target with
    | Applied _ -> Ok 0
    | Pending migration ->
      match
        pending
        |> Seq.tryFindIndex(fun candidate ->
          candidate.timestamp = migration.timestamp
          && candidate.name = migration.name)
      with
      | Some index -> Ok(index + 1)
      | None ->
        Error
          $"Migration '{migration.name}' is not pending, cannot apply to it."

  /// Amount for `down --to`: applied migrations roll back newest first,
  /// so the amount rolls back everything newer than the target, leaving
  /// the target applied. A pending target cannot be rolled back to.
  let downTargetAmount(target: MigrationStatus, applied: Migration seq) =
    match target with
    | Pending migration ->
      Error
        $"Migration '{migration.name}' is not applied, cannot roll back to it."
    | Applied migration ->
      match
        applied
        |> Seq.tryFindIndex(fun candidate ->
          candidate.timestamp = migration.timestamp
          && candidate.name = migration.name)
      with
      | Some index -> Ok index
      | None ->
        Error
          $"Migration '{migration.name}' is not applied, cannot roll back to it."

  let runTargetUp(logger: ILogger, migrondi: IMigrondi, args: TargetRunArgs) =
    match resolveTarget(migrondi.MigrationsList(), args.Target) with
    | Error message ->
      logger.LogError("{Message}", message)
      1
    | Ok target ->
      match upTargetAmount(target, migrondi.DryRunUp()) with
      | Error message ->
        logger.LogError("{Message}", message)
        1
      | Ok amount ->
        if args.DryRun then
          runDryUp(Some amount, logger, migrondi)
        else
          runUp(Some amount, logger, migrondi)

  let runTargetDown(logger: ILogger, migrondi: IMigrondi, args: TargetRunArgs) =
    match resolveTarget(migrondi.MigrationsList(), args.Target) with
    | Error message ->
      logger.LogError("{Message}", message)
      1
    | Ok target ->
      match downTargetAmount(target, migrondi.DryRunDown()) with
      | Error message ->
        logger.LogError("{Message}", message)
        1
      | Ok amount ->
        if args.DryRun then
          runDryDown(Some amount, logger, migrondi)
        else
          runDown(Some amount, logger, migrondi)

  let listMigrations(logger: ILogger, migrondi: IMigrondi, args: ListArgs) =

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

    let showDetails(selected: MigrationStatus list) =
      for status in selected do
        let migration = status.Value

        if args.Filters.ShowPath then
          let fileName = migrationFileName migration

          logger.LogInformation(
            "Migration {MigrationName} is at {MigrationPath}",
            migration.name,
            Path.Combine(args.MigrationsDir, fileName)
          )

        if args.Filters.ShowContent then
          logger.LogInformation(
            "Migration {MigrationName}\n------ UP ------\n{UpContent}\n------ DOWN ------\n{DownContent}",
            migration.name,
            migration.upContent,
            migration.downContent
          )

    match
      statusSelection args.Filters,
      filterMigrations(args.Filters, migrondi.MigrationsList())
    with
    | Error message, _
    | _, Error message ->
      logger.LogError("{Message}", message)
      1
    | Ok selection, Ok selected ->
      let migrations = selected |> List.map(fun status -> status.Value)

      match selection with
      | AppliedOnly ->
        if args.UseJson then
          printJson(migrations, "Applied")
        else
          let table = Table()
          table.Title <- TableTitle("Applied Migrations")
          printMigrationsTable(table, migrations)
      | PendingOnly ->
        if args.UseJson then
          printJson(migrations, "Pending")
        else
          let table = Table()
          table.Title <- TableTitle("Pending Migrations")
          printMigrationsTable(table, migrations)
      | AllMigrations ->
        if args.UseJson then
          printJsonBoth(selected)
        else
          let table = Table()
          table.Title <- TableTitle("All Migrations")
          printBothMigrationsTable(table, selected)

      showDetails selected
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

      1
