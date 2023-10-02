namespace Migrondi.Core.Migrondi

open System.Collections.Generic
open System.Threading.Tasks
open System.Runtime.InteropServices


open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open System.Threading
open Microsoft.Extensions.Logging

open FsToolkit.ErrorHandling

open IcedTasks

[<Interface>]
type MigrondiService =

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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
    (migrationPath: string)
    =
    let migration = fs.ReadMigration migrationPath

    match db.FindMigration migration.name with
    | Some _ -> Applied migration
    | None -> Pending migration

  let runUpAsync
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
    (db: DatabaseService)
    (fs: FileSystemService)
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
type MigrondiServiceFactory =

  static member GetInstance
    (
      database: #DatabaseService,
      fileSystem: #FileSystemService,
      logger: #ILogger,
      config: MigrondiConfig
    ) =
    { new MigrondiService with
        member _.DryRunUp([<Optional>] ?amount) : IReadOnlyList<Migration> =
          MigrondiserviceImpl.runDryUp database fileSystem config amount

        member _.DryRunDown([<Optional>] ?amount) : IReadOnlyList<Migration> =
          MigrondiserviceImpl.runDryDown database fileSystem config amount

        member _.RunDown
          ([<Optional>] ?amount)
          : IReadOnlyList<MigrationRecord> =
          MigrondiserviceImpl.runDown database fileSystem logger config amount

        member _.RunUp([<Optional>] ?amount) : IReadOnlyList<MigrationRecord> =
          MigrondiserviceImpl.runUp database fileSystem logger config amount

        member _.MigrationsList() : IReadOnlyList<MigrationStatus> =
          MigrondiserviceImpl.migrationsList database fileSystem config

        member _.ScriptStatus(arg1: string) : MigrationStatus =
          MigrondiserviceImpl.scriptStatus database fileSystem arg1

        member _.RunUpAsync
          (
            [<Optional>] ?amount,
            [<Optional>] ?cancellationToken
          ) =
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

          MigrondiserviceImpl.dryRunUpAsync
            database
            fileSystem
            config
            amount
            token

        member _.MigrationsListAsync([<Optional>] ?cancellationToken) =
          let token = defaultArg cancellationToken CancellationToken.None

          MigrondiserviceImpl.migrationsListAsync
            database
            fileSystem
            config
            token

        member _.ScriptStatusAsync
          (
            arg1: string,
            [<Optional>] ?cancellationToken
          ) =
          let token = defaultArg cancellationToken CancellationToken.None

          MigrondiserviceImpl.scriptStatusAsync database fileSystem arg1 token
    }