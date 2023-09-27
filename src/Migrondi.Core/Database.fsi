namespace Migrondi.Core.Database

open System
open System.Collections.Generic
open System.Data
open Microsoft.Data.SqlClient
open Microsoft.Data.Sqlite
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

open MySqlConnector
open Npgsql

open RepoDb
open RepoDb.Enumerations
open Serilog

open FsToolkit.ErrorHandling

open Migrondi.Core

/// <summary>
/// This service talks directly to the database and is responsible, it provides means to setting up the database
/// before it's first usage, that is important because it will create the migrations table that is used to keep track
/// of the applied migrations.
/// This service also provides both sync and async methods to list, apply and rollback migrations.
/// </summary>
[<Interface>]
type DatabaseService =

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  /// <exception cref="Migrondi.Core.Database.Exceptions.SetupDatabaseFailed">
  /// Thrown when the setup of the database failed
  /// </exception>
  abstract member SetupDatabase: unit -> unit

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigration: name: string -> MigrationRecord option

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastApplied: unit -> MigrationRecord option

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrations: unit -> MigrationRecord IReadOnlyList

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were applied to the database
  /// </returns>
  /// <remarks>
  /// This Method will throw an <see cref="MigrationApplicationFailed"/> exception if
  /// it fails to apply a migration
  /// </remarks>
  abstract member ApplyMigrations: migrations: Migration seq -> MigrationRecord IReadOnlyList

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  /// <remarks>
  /// This Method will throw an <see cref="MigrationRollbackFailed"/> exception if
  /// it fails to rollback a migration
  /// </remarks>
  abstract member RollbackMigrations: migrations: Migration seq -> MigrationRecord IReadOnlyList

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabaseAsync: [<Optional>] ?cancellationToken: CancellationToken -> Task

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <param name="cancellationToken">A cancellation token</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigrationAsync:
    name: string * [<Optional>] ?cancellationToken: CancellationToken -> Task<MigrationRecord option>

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastAppliedAsync:
    [<Optional>] ?cancellationToken: CancellationToken -> Task<MigrationRecord option>

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrationsAsync:
    [<Optional>] ?cancellationToken: CancellationToken -> Task<MigrationRecord IReadOnlyList>

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were applied to the database
  /// </returns>
  abstract member ApplyMigrationsAsync:
    migrations: Migration seq * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationRecord IReadOnlyList>

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  abstract member RollbackMigrationsAsync:
    migrations: Migration seq * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationRecord IReadOnlyList>

[<RequireQualifiedAccess>]
module private Queries =
  val createTable: driver: MigrondiDriver -> tableName: string -> string

module private MigrationsImpl =
  val getConnection: connectionString: string * driver: MigrondiDriver -> IDbConnection
  val initializeDriver: driver: MigrondiDriver -> unit
  val setupDatabase: connection: IDbConnection -> driver: MigrondiDriver -> tableName: string -> unit
  val findMigration: connection: IDbConnection -> tableName: string -> name: 'a -> MigrationRecord option
  val findLastApplied: connection: IDbConnection -> tableName: string -> MigrationRecord option
  val listMigrations: connection: IDbConnection -> tableName: string -> MigrationRecord list

  val applyMigrations:
    connection: IDbConnection ->
    logger: ILogger ->
    tableName: string ->
    migrations: Migration list ->
      MigrationRecord list

  val rollbackMigrations:
    connection: IDbConnection ->
    logger: ILogger ->
    tableName: string ->
    migrations: Migration list ->
      MigrationRecord list

module private MigrationsAsyncImpl =
  val setupDatabaseAsync: connection: IDbConnection -> driver: MigrondiDriver -> tableName: string -> Async<unit>
  val findMigrationAsync: connection: IDbConnection -> tableName: string -> name: 'a -> Async<MigrationRecord option>
  val findLastAppliedAsync: connection: IDbConnection -> tableName: string -> Async<MigrationRecord option>
  val listMigrationsAsync: connection: IDbConnection -> tableName: string -> Async<IReadOnlyList<MigrationRecord>>

  val applyMigrationsAsync:
    connection: IDbConnection ->
    logger: ILogger ->
    tableName: string ->
    migrations: Migration list ->
      Async<IReadOnlyList<MigrationRecord>>

  val rollbackMigrationsAsync:
    connection: IDbConnection ->
    logger: ILogger ->
    tableName: string ->
    migrations: Migration list ->
      Async<IReadOnlyList<MigrationRecord>>

[<Class>]
type DatabaseImpl =
  /// <summary>
  /// Generates a new database service, this can be further customized by passing in a custom logger
  /// instance and a custom configuration.
  /// </summary>
  /// <param name="logger">A Serilog compatible ILogger this is used for the lifetime of the service.</param>
  /// <param name="config">A configuration object to be able to find the connection string for the database.</param>
  /// <returns>
  /// A new database service instance
  /// </returns>
  static member Build: logger: ILogger * config: MigrondiConfig -> DatabaseService