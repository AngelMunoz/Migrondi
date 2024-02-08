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

open FsToolkit.ErrorHandling

open IcedTasks

[<Interface>]
type IMigrondi =

  abstract member Initialize: unit -> unit

  abstract member InitializeAsync:
    [<Optional>] ?cancellationToken: CancellationToken -> Task

  /// <summary>
  /// Creates a new migration file with
  /// the default naming convention and returns it
  /// </summary>
  /// <param name="friendlyName">
  /// The friendly name of the migration, usually this comes from
  /// the user's input
  /// </param>
  /// <param name="upContent">
  /// The content of the up migration
  /// </param>
  /// <param name="downContent">
  /// The content of the down migration
  /// </param>
  /// <returns>
  /// The newly created migration as a record
  /// </returns>
  abstract member RunNew:
    friendlyName: string *
    [<Optional>] ?upContent: string *
    [<Optional>] ?downContent: string ->
      Migration

  /// <summary>
  /// Creates a new migration file with
  /// the default naming convention and returns it
  /// </summary>
  /// <param name="friendlyName">
  /// The friendly name of the migration, usually this comes from
  /// the user's input
  /// </param>
  /// <param name="upContent">
  /// The content of the up migration
  /// </param>
  /// <param name="downContent">
  /// The content of the down migration
  /// </param>
  /// <param name="cancellationToken">
  /// A cancellation token to cancel the operation
  /// </param>
  /// <returns>
  /// The newly created migration as a record
  /// </returns>
  abstract member RunNewAsync:
    friendlyName: string *
    [<Optional>] ?upContent: string *
    [<Optional>] ?downContent: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration>


  /// <summary>
  /// Runs all pending migrations against the database
  /// </summary>
  /// <param name="amount">The amount of migrations to apply</param>
  /// <returns>
  /// A list of all migrations that were applied including previously applied ones
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  abstract member RunUp:
    [<Optional>] ?amount: int -> IReadOnlyList<MigrationRecord>

  /// <summary>
  /// Reverts all migrations that were previously applied
  /// </summary>
  /// <param name="amount">The amount of migrations to roll back</param>
  /// <returns>
  /// A list of all migrations that were reverted including previously applied ones
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  abstract member RunDown:
    [<Optional>] ?amount: int -> IReadOnlyList<MigrationRecord>

  /// <summary>
  /// Makes a list of the pending migrations that would be applied
  /// </summary>
  /// <param name="amount">The amount of migrations to apply</param>
  /// <returns>
  /// A list of all migrations that would be applied
  /// </returns>
  abstract member DryRunUp: [<Optional>] ?amount: int -> Migration IReadOnlyList

  /// <summary>
  /// Makes a list of the pending migrations that would be reverted
  /// </summary>
  /// <param name="amount">The amount of migrations to roll back</param>
  /// <returns>
  /// A list of all migrations that would be reverted
  /// </returns>
  abstract member DryRunDown:
    [<Optional>] ?amount: int -> Migration IReadOnlyList

  /// <summary>
  /// Makes a list of all migrations and their status
  /// </summary>
  /// <returns>
  /// A list of all migrations and their status
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  abstract member MigrationsList: unit -> MigrationStatus IReadOnlyList

  /// <summary>
  /// Takes a relative path to the migrations dir to a migration file
  /// and returns its status
  /// </summary>
  /// <param name="migrationPath">The relative path to the migration file</param>
  /// <returns>
  /// The status of the migration
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
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

module private MigrondiserviceImpl =


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
    |> Array.sortBy(fun migration -> migration.timestamp)

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
    |> Array.sortByDescending(fun migration -> migration.timestamp)


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
      (appliedMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
    )

    let pendingMigrations = obtainPendingUp migrations appliedMigrations

    logger.LogDebug(
      "Pending migrations: {Migrations}",
      (pendingMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
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
      (appliedMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
    )

    let pendingMigrations = obtainPendingDown migrations appliedMigrations

    logger.LogDebug(
      "Rolling back migrations: {Migrations}",
      (pendingMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
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
        (appliedMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
      )

      let pendingMigrations = obtainPendingUp migrations appliedMigrations

      logger.LogDebug(
        "Pending migrations: {Migrations}",
        (pendingMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
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
        (appliedMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
      )

      let pendingMigrations = obtainPendingDown migrations appliedMigrations

      logger.LogDebug(
        "Rolling back migrations: {Migrations}",
        (pendingMigrations |> Seq.map(fun m -> m.name) |> String.concat ", ")
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
    (logger: ILogger)
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
  }


  static member MigrondiFactory(logger: ILogger) =
    Func<_, _, _, _>(fun
                         (config: MigrondiConfig)
                         (projectRoot: Uri)
                         (migrationsDIr: Uri) ->
      let database = MiDatabaseHandler(logger, config)
      let serializer = MigrondiSerializer()

      let fileSystem =
        MiFileSystem(
          logger,
          serializer,
          serializer,
          projectRoot,
          migrationsDIr
        )

      new Migrondi(config, database, fileSystem, logger) :> IMigrondi
    )

  interface IMigrondi with

    member _.Initialize() = database.SetupDatabase()

    member _.InitializeAsync([<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None
      database.SetupDatabaseAsync(token)

    member _.RunNew
      (
        friendlyName,
        [<Optional>] ?upContent,
        [<Optional>] ?downContent
      ) : Migration =
      let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
      let name = $"{friendlyName}_{timestamp}.sql"

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
        let name = $"{friendlyName}_{timestamp}.sql"
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
      (
        [<Optional>] ?amount,
        [<Optional>] ?cancellationToken
      ) =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.runDownAsync
        database
        fileSystem
        logger
        config
        amount
        token

    member _.DryRunDownAsync
      (
        [<Optional>] ?amount,
        [<Optional>] ?cancellationToken
      ) =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.dryRunDownAsync
        database
        fileSystem
        logger
        config
        amount
        token

    member _.DryRunUpAsync
      (
        [<Optional>] ?amount,
        [<Optional>] ?cancellationToken
      ) =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.dryRunUpAsync database fileSystem config amount token

    member _.MigrationsListAsync([<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.migrationsListAsync database fileSystem config token

    member _.ScriptStatusAsync(arg1: string, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      MigrondiserviceImpl.scriptStatusAsync database fileSystem arg1 token