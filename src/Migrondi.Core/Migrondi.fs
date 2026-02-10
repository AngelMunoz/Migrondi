namespace Migrondi.Core

open System
open System.Collections.Generic
open System.Threading.Tasks
open System.Runtime.InteropServices

open Migrondi.Core
open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open System.Threading
open Microsoft.Extensions.Logging

open IcedTasks

[<Interface>]
type IMigrondi =

  abstract member Initialize: unit -> unit

  abstract member InitializeAsync:
    [<Optional>] ?cancellationToken: CancellationToken -> Task

  abstract member RunNew:
    friendlyName: string *
    [<Optional>] ?upContent: string *
    [<Optional>] ?downContent: string ->
      Migration

  abstract member RunNewAsync:
    friendlyName: string *
    [<Optional>] ?upContent: string *
    [<Optional>] ?downContent: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration>

  abstract member RunUp:
    [<Optional>] ?amount: int -> IReadOnlyList<MigrationRecord>

  abstract member RunDown:
    [<Optional>] ?amount: int -> IReadOnlyList<MigrationRecord>

  abstract member DryRunUp: [<Optional>] ?amount: int -> Migration IReadOnlyList

  abstract member DryRunDown:
    [<Optional>] ?amount: int -> Migration IReadOnlyList

  abstract member MigrationsList: unit -> MigrationStatus IReadOnlyList

  abstract member ScriptStatus: migrationPath: string -> MigrationStatus

  abstract member RunUpAsync:
    [<Optional>] ?amount: int *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<IReadOnlyList<MigrationRecord>>

  abstract member RunDownAsync:
    [<Optional>] ?amount: int *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<IReadOnlyList<MigrationRecord>>

  abstract member DryRunUpAsync:
    [<Optional>] ?amount: int *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration IReadOnlyList>

  abstract member DryRunDownAsync:
    [<Optional>] ?amount: int *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration IReadOnlyList>

  abstract member MigrationsListAsync:
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationStatus IReadOnlyList>

  abstract member ScriptStatusAsync:
    string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationStatus>

module internal MigrondiserviceImpl =

  let internal getConnectionStr (rootPath: string) (config: MigrondiConfig) =
    match config.driver with
    | MigrondiDriver.Sqlite ->
      let prefix = "Data Source="

      let idx =
        config.connection.IndexOf(prefix, StringComparison.OrdinalIgnoreCase)

      if idx >= 0 then
        let afterPrefix = config.connection.Substring(idx + prefix.Length)
        let semiIdx = afterPrefix.IndexOf(';')

        let dbPath, rest =
          if semiIdx >= 0 then
            afterPrefix.Substring(0, semiIdx), afterPrefix.Substring(semiIdx)
          else
            afterPrefix, ""

        let dbPathTrimmed = dbPath.Trim()

        let isWindowsRooted (path: string) =
          path.Length >= 3
          && Char.IsLetter(path[0])
          && path[1] = ':'
          && (path[2] = '\\' || path[2] = '/')

        if
          System.IO.Path.IsPathRooted(dbPathTrimmed)
          || isWindowsRooted dbPathTrimmed
        then
          config.connection
        else
          let rootedPath = System.IO.Path.Combine(rootPath, dbPathTrimmed)

          config.connection.Substring(0, idx + prefix.Length)
          + rootedPath
          + rest
      else
        config.connection
    | _ -> config.connection

  let obtainPendingUp
    (migrations: IReadOnlyList<Migration>)
    (appliedMigrations: IReadOnlyList<MigrationRecord>)
    =
    migrations
    |> Seq.toArray
    |> Array.Parallel.choose(fun migration ->
      match
        appliedMigrations
        |> Seq.tryFind(fun applied ->
          applied.name = migration.name
          && applied.timestamp = migration.timestamp
        )
      with
      | Some _ -> None
      | None -> Some migration
    )
    |> Array.sortBy(_.timestamp)

  let obtainPendingDown
    (migrations: IReadOnlyList<Migration>)
    (appliedMigrations: IReadOnlyList<MigrationRecord>)
    =
    migrations
    |> Seq.toArray
    |> Array.Parallel.choose(fun migration ->
      match
        appliedMigrations
        |> Seq.tryFind(fun applied ->
          applied.name = migration.name
          && applied.timestamp = migration.timestamp
        )
      with
      | Some _ -> Some migration
      | None -> None
    )
    |> Array.sortByDescending(_.timestamp)

  let runUp
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (logger: ILogger)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let migrations = fs.ListMigrations config.migrations
    let appliedMigrations = db.ListMigrations()

    logger.LogDebug(
      "Applied migrations: {Migrations}",
      (appliedMigrations |> Seq.map(_.name) |> String.concat ", ")
    )

    let pendingMigrations = obtainPendingUp migrations appliedMigrations

    logger.LogDebug(
      "Pending migrations: {Migrations}",
      (pendingMigrations |> Seq.map(_.name) |> String.concat ", ")
    )

    let migrationsToRun =
      match amount with
      | Some amount ->
        if amount >= 0 && amount < pendingMigrations.Length then
          pendingMigrations |> Array.take amount
        else
          logger.LogWarning
            "The amount specified is out of bounds in relation with the pending migrations. Running all pending migrations."

          pendingMigrations
      | None -> pendingMigrations

    logger.LogInformation $"Running '%i{pendingMigrations.Length}' migrations."
    db.ApplyMigrations migrationsToRun

  let runDown
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (logger: ILogger)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let appliedMigrations = db.ListMigrations()
    let migrations = fs.ListMigrations config.migrations

    logger.LogDebug(
      "Applied migrations: {Migrations}",
      (appliedMigrations |> Seq.map(_.name) |> String.concat ", ")
    )

    let pendingMigrations = obtainPendingDown migrations appliedMigrations

    logger.LogDebug(
      "Rolling back migrations: {Migrations}",
      (pendingMigrations |> Seq.map(_.name) |> String.concat ", ")
    )

    let migrationsToRun =
      match amount with
      | Some amount ->
        if amount >= 0 && amount < pendingMigrations.Length then
          pendingMigrations |> Array.take amount
        else
          logger.LogWarning
            "The amount specified is out of bounds in relation with the pending migrations. Rolling back all pending migrations."

          pendingMigrations
      | None -> pendingMigrations

    logger.LogInformation(
      "Reverting '{MigrationAmount}' migrations.",
      pendingMigrations.Length
    )

    db.RollbackMigrations migrationsToRun

  let runDryUp
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let migrations = fs.ListMigrations config.migrations
    let appliedMigrations = db.ListMigrations()

    let pending = obtainPendingUp migrations appliedMigrations |> List.ofArray

    match amount with
    | Some amount ->
      let migrations =
        if amount >= 0 && amount < pending.Length then
          pending |> List.take amount
        else
          pending

      migrations :> IReadOnlyList<Migration>
    | None -> pending

  let runDryDown
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let appliedMigrations = db.ListMigrations()
    let migrations = fs.ListMigrations config.migrations

    let pending = obtainPendingDown migrations appliedMigrations |> List.ofArray

    match amount with
    | Some amount ->
      let migrations =
        if amount >= 0 && amount < pending.Length then
          pending |> List.take amount
        else
          pending

      migrations :> IReadOnlyList<Migration>
    | None -> pending

  let migrationsList
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (config: MigrondiConfig)
    =
    let migrations = fs.ListMigrations config.migrations
    let appliedMigrations = db.ListMigrations()

    migrations
    |> List.ofSeq
    |> List.map(fun migration ->
      match
        appliedMigrations
        |> Seq.tryFind(fun applied ->
          applied.name = migration.name
          && applied.timestamp = migration.timestamp
        )
      with
      | Some _ -> Applied migration
      | None -> Pending migration
    )
    :> IReadOnlyList<MigrationStatus>

  let scriptStatus
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (migrationPath: string)
    =
    let migration = fs.ReadMigration migrationPath

    match db.FindMigration migration.name with
    | Some _ -> Applied migration
    | None -> Pending migration

  let runUpAsync
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (logger: ILogger)
    (config: MigrondiConfig)
    (amount: int option)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()

      let! migrations = fs.ListMigrationsAsync(config.migrations, token)

      and! appliedMigrations = db.ListMigrationsAsync(token)

      logger.LogDebug(
        "Applied migrations: {Migrations}",
        (appliedMigrations |> Seq.map(_.name) |> String.concat ", ")
      )

      let pendingMigrations = obtainPendingUp migrations appliedMigrations

      logger.LogDebug(
        "Pending migrations: {Migrations}",
        (pendingMigrations |> Seq.map(_.name) |> String.concat ", ")
      )

      let migrationsToRun =
        match amount with
        | Some amount ->
          if amount >= 0 && amount < pendingMigrations.Length then
            pendingMigrations |> Array.take amount
          else
            logger.LogWarning
              "The amount specified is out of bounds in relation with the pending migrations. Running all pending migrations."

            pendingMigrations
        | None -> pendingMigrations

      logger.LogInformation
        $"Running '%i{pendingMigrations.Length}' migrations."

      return! db.ApplyMigrationsAsync(migrationsToRun, token)
    }

  let runDownAsync
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (logger: ILogger)
    (config: MigrondiConfig)
    (amount: int option)
    =
    cancellableTask {

      let! token = CancellableTask.getCancellationToken()

      let! appliedMigrations = db.ListMigrationsAsync(token)
      and! migrations = fs.ListMigrationsAsync(config.migrations, token)

      logger.LogDebug(
        "Applied migrations: {Migrations}",
        (appliedMigrations |> Seq.map(_.name) |> String.concat ", ")
      )

      let pendingMigrations = obtainPendingDown migrations appliedMigrations

      logger.LogDebug(
        "Rolling back migrations: {Migrations}",
        (pendingMigrations |> Seq.map(_.name) |> String.concat ", ")
      )

      let migrationsToRun =
        match amount with
        | Some amount ->
          if amount >= 0 && amount < pendingMigrations.Length then
            pendingMigrations |> Array.take amount
          else
            logger.LogWarning
              "The amount specified is out of bounds in relation with the pending migrations. Rolling back all pending migrations."

            pendingMigrations
        | None -> pendingMigrations

      logger.LogInformation(
        "Reverting '{MigrationAmount}' migrations.",
        pendingMigrations.Length
      )

      return! db.RollbackMigrationsAsync migrationsToRun
    }


  let dryRunUpAsync
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (config: MigrondiConfig)
    (amount: int option)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let! migrations = fs.ListMigrationsAsync(config.migrations, token)
      and! appliedMigrations = db.ListMigrationsAsync(token)

      let pending = obtainPendingUp migrations appliedMigrations |> List.ofArray

      return
        match amount with
        | Some amount ->
          let migrations =
            if amount >= 0 && amount < pending.Length then
              pending |> List.take amount
            else
              pending

          migrations :> IReadOnlyList<Migration>
        | None -> pending
    }

  let dryRunDownAsync
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (config: MigrondiConfig)
    (amount: int option)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()

      let! appliedMigrations = db.ListMigrationsAsync(token)
      and! migrations = fs.ListMigrationsAsync(config.migrations, token)

      let pending =
        obtainPendingDown migrations appliedMigrations |> List.ofArray

      return
        match amount with
        | Some amount ->
          let migrations =
            if amount >= 0 && amount < pending.Length then
              pending |> List.take amount
            else
              pending

          migrations :> IReadOnlyList<Migration>
        | None -> pending
    }

  let migrationsListAsync
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (config: MigrondiConfig)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let! migrations = fs.ListMigrationsAsync(config.migrations, token)
      and! appliedMigrations = db.ListMigrationsAsync(token)

      let categorizeMigrations =
        fun migration ->
          match
            appliedMigrations
            |> Seq.tryFind(fun applied ->
              applied.name = migration.name
              && applied.timestamp = migration.timestamp
            )
          with
          | Some _ -> Applied migration
          | None -> Pending migration

      return
        migrations |> List.ofSeq |> List.map categorizeMigrations
        :> IReadOnlyList<MigrationStatus>
    }

  let scriptStatusAsync
    (db: IMiDatabaseHandler)
    (fs: IMiFileSystem)
    (migrationPath: string)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let! migration = fs.ReadMigrationAsync(migrationPath, token)

      match! db.FindMigrationAsync(migration.name, token) with
      | Some _ -> return Applied migration
      | None -> return Pending migration
    }

  let defaultLoggerFactory =
    lazy
      LoggerFactory.Create(fun builder ->
        builder
#if DEBUG
          .SetMinimumLevel(LogLevel.Debug)
#else
          .SetMinimumLevel(LogLevel.Information)
#endif
          .AddSimpleConsole()
        |> ignore
      )

[<Class>]
type Migrondi
  (
    config: MigrondiConfig,
    database: IMiDatabaseHandler,
    fileSystem: IMiFileSystem,
    logger: ILogger
  ) =

  let getMigration (name, timestamp, upContent, downContent) = {
    name = name
    timestamp = timestamp
    upContent =
      defaultArg
        upContent
        "-- Add your SQL migration code below. You can delete this line but do not delete the comments above.\n\n"
    downContent =
      defaultArg
        downContent
        "-- Add your SQL rollback code below. You can delete this line but do not delete the comment above.\n\n"
    manualTransaction = false
  }

  static member MigrondiFactory
    (
      config: MigrondiConfig,
      rootDirectory: string,
      [<Optional>] ?logger: ILogger<IMigrondi>,
      [<Optional>] ?migrationSource: IMiMigrationSource
    ) : IMigrondi =

    let logger =
      defaultArg
        logger
        (MigrondiserviceImpl.defaultLoggerFactory.Value.CreateLogger<IMigrondi>())

    let config = {
      config with
          connection = MigrondiserviceImpl.getConnectionStr rootDirectory config
    }

    let database = MiDatabaseHandler(logger, config)
    let serializer = MigrondiSerializer()

    let projectRoot =
      let rootDirectory = IO.Path.GetFullPath(rootDirectory)

      if IO.Path.EndsInDirectorySeparator rootDirectory then
        Uri(rootDirectory, UriKind.Absolute)
      else
        Uri(
          $"{rootDirectory}{IO.Path.DirectorySeparatorChar}",
          UriKind.Absolute
        )

    let migrationsDir =
      if IO.Path.EndsInDirectorySeparator config.migrations then
        Uri(config.migrations, UriKind.Relative)
      else
        Uri(
          $"{config.migrations}{IO.Path.DirectorySeparatorChar}",
          UriKind.Relative
        )

    let fileSystem =
      MiFileSystem(
        logger,
        serializer,
        serializer,
        projectRoot,
        migrationsDir,
        ?source = migrationSource
      )

    Migrondi(config, database, fileSystem, logger)

  interface IMigrondi with

    member _.Initialize() = database.SetupDatabase()

    member _.InitializeAsync([<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None
      database.SetupDatabaseAsync(token)

    member _.RunNew
      (friendlyName, [<Optional>] ?upContent, [<Optional>] ?downContent)
      : Migration =
      let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
      let name = $"{timestamp}_{friendlyName}.sql"

      let migration = getMigration(name, timestamp, upContent, downContent)

      fileSystem.WriteMigration(migration, name)
      migration

    member _.RunNewAsync
      (
        friendlyName,
        [<Optional>] ?upContent,
        [<Optional>] ?downContent,
        [<Optional>] ?cancellationToken
      ) =
      let token = defaultArg cancellationToken CancellationToken.None

      task {
        let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        let name = $"{timestamp}_{friendlyName}.sql"
        let migration = getMigration(name, timestamp, upContent, downContent)
        do! fileSystem.WriteMigrationAsync(migration, name, token)

        return migration
      }

    member _.DryRunUp([<Optional>] ?amount) : IReadOnlyList<Migration> =
      MigrondiserviceImpl.runDryUp database fileSystem config amount

    member _.DryRunDown([<Optional>] ?amount) : IReadOnlyList<Migration> =
      MigrondiserviceImpl.runDryDown database fileSystem config amount

    member _.RunDown([<Optional>] ?amount) : IReadOnlyList<MigrationRecord> =
      MigrondiserviceImpl.runDown database fileSystem logger config amount

    member _.RunUp([<Optional>] ?amount) : IReadOnlyList<MigrationRecord> =
      MigrondiserviceImpl.runUp database fileSystem logger config amount

    member _.MigrationsList() : IReadOnlyList<MigrationStatus> =
      MigrondiserviceImpl.migrationsList database fileSystem config

    member _.ScriptStatus(arg1: string) : MigrationStatus =
      MigrondiserviceImpl.scriptStatus database fileSystem arg1

    member _.RunUpAsync([<Optional>] ?amount, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.runUpAsync
        database
        fileSystem
        logger
        config
        amount
        token

    member _.RunDownAsync
      ([<Optional>] ?amount, [<Optional>] ?cancellationToken)
      =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.runDownAsync
        database
        fileSystem
        logger
        config
        amount
        token

    member _.DryRunDownAsync
      ([<Optional>] ?amount, [<Optional>] ?cancellationToken)
      =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.dryRunDownAsync
        database
        fileSystem
        config
        amount
        token

    member _.DryRunUpAsync
      ([<Optional>] ?amount, [<Optional>] ?cancellationToken)
      =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.dryRunUpAsync database fileSystem config amount token

    member _.MigrationsListAsync([<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None
      MigrondiserviceImpl.migrationsListAsync database fileSystem config token

    member _.ScriptStatusAsync(arg1: string, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None
      MigrondiserviceImpl.scriptStatusAsync database fileSystem arg1 token