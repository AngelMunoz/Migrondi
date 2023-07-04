namespace Migrondi.Core.Migrondi

open System.Collections.Generic
open System.Threading.Tasks
open System.Runtime.InteropServices


open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open System.Threading


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
    [<Optional>] ?amount: int -> MigrationRecord IReadOnlyList

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
    [<Optional>] ?amount: int -> MigrationRecord IReadOnlyList

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
      Task<Migration IReadOnlyList>

  abstract member RunDownAsync:
    [<Optional>] ?amount: int *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration IReadOnlyList>

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

module MigrondiEnvImpl =
  open FsToolkit.ErrorHandling

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
    (db: #DatabaseEnv)
    (fs: #FileSystemEnv)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let migrations = fs.ListMigrations config.migrations
    let appliedMigrations = db.ListMigrations()

    let pendingMigrations = obtainPendingUp migrations appliedMigrations
    let amount = defaultArg amount pendingMigrations.Length

    db.ApplyMigrations pendingMigrations[0..amount]

  let runDown
    (db: #DatabaseEnv)
    (fs: #FileSystemEnv)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let appliedMigrations = db.ListMigrations()
    let migrations = fs.ListMigrations config.migrations

    let pendingMigrations = obtainPendingDown migrations appliedMigrations
    let amount = defaultArg amount pendingMigrations.Length

    db.RollbackMigrations pendingMigrations[0..amount]

  let runDryUp
    (db: #DatabaseEnv)
    (fs: #FileSystemEnv)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let migrations = fs.ListMigrations config.migrations
    let appliedMigrations = db.ListMigrations()

    let pending = obtainPendingUp migrations appliedMigrations
    let amount = defaultArg amount pending.Length

    pending[0..amount]

  let runDryDown
    (db: #DatabaseEnv)
    (fs: #FileSystemEnv)
    (config: MigrondiConfig)
    (amount: int option)
    =
    let appliedMigrations = db.ListMigrations()
    let migrations = fs.ListMigrations config.migrations

    let pending = obtainPendingDown migrations appliedMigrations
    let amount = defaultArg amount pending.Length

    pending[0..amount]

  let migrationsList
    (db: #DatabaseEnv)
    (fs: #FileSystemEnv)
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
    (db: #DatabaseEnv)
    (fs: #FileSystemEnv)
    (migrationPath: string)
    =
    let migration = fs.ReadMigration migrationPath

    match db.FindMigration migration.name with
    | Some _ -> Applied migration
    | None -> Pending migration

[<Class>]
type MigrondiServiceImpl =

  static member BuildDefaultEnv
    (
      database: #DatabaseEnv,
      fileSystem: #FileSystemEnv,
      config: MigrondiConfig
    ) =
    { new MigrondiService with
        member _.DryRunUp([<Optional>] ?amount) : IReadOnlyList<Migration> =
          MigrondiEnvImpl.runDryDown database fileSystem config amount

        member _.DryRunDown([<Optional>] ?amount) : IReadOnlyList<Migration> =
          MigrondiEnvImpl.runDryDown database fileSystem config amount

        member _.RunDown
          ([<Optional>] ?amount)
          : IReadOnlyList<MigrationRecord> =
          MigrondiEnvImpl.runDown database fileSystem config amount

        member _.RunUp([<Optional>] ?amount) : IReadOnlyList<MigrationRecord> =
          MigrondiEnvImpl.runUp database fileSystem config amount

        member _.MigrationsList() : IReadOnlyList<MigrationStatus> =
          MigrondiEnvImpl.migrationsList database fileSystem config

        member _.ScriptStatus(arg1: string) : MigrationStatus =
          MigrondiEnvImpl.scriptStatus database fileSystem arg1

        member _.RunUpAsync
          (
            [<Optional>] ?amount,
            [<Optional>] ?cancellationToken
          ) : Task<IReadOnlyList<Migration>> =
          failwith "Not Implemented"

        member _.RunDownAsync
          (
            [<Optional>] ?amount,
            [<Optional>] ?cancellationToken
          ) : Task<IReadOnlyList<Migration>> =
          failwith "Not Implemented"

        member _.DryRunDownAsync
          (
            [<Optional>] ?amount,
            [<Optional>] ?cancellationToken
          ) : Task<IReadOnlyList<Migration>> =
          failwith "Not Implemented"

        member _.DryRunUpAsync
          (
            [<Optional>] ?amount,
            [<Optional>] ?cancellationToken
          ) : Task<IReadOnlyList<Migration>> =
          failwith "Not Implemented"

        member _.MigrationsListAsync
          ([<Optional>] ?cancellationToken)
          : Task<IReadOnlyList<MigrationStatus>> =
          failwith "Not Implemented"

        member _.ScriptStatusAsync
          (
            arg1: string,
            [<Optional>] ?cancellationToken
          ) : Task<MigrationStatus> =
          failwith "Not Implemented"
    }