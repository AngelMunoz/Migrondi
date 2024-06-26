namespace Migrondi.Core

open System
open System.Collections.Generic
open System.Threading.Tasks
open System.Runtime.InteropServices

open Microsoft.Extensions.Logging

open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open System.Threading

/// <summary>
/// This is the main service that coordinates all of the other parts of this library.
/// The main purpose of the Migrondi service is to provide a simple and straight forward way to
/// Operate against the databases and the file system.
/// </summary>
[<Interface>]
type IMigrondi =

  abstract member Initialize: unit -> unit


  abstract member InitializeAsync: [<Optional>] ?cancellationToken: CancellationToken -> Task

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
    friendlyName: string * [<Optional>] ?upContent: string * [<Optional>] ?downContent: string -> Migration

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
  abstract member RunUp: [<Optional>] ?amount: int -> MigrationRecord IReadOnlyList

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
  abstract member RunDown: [<Optional>] ?amount: int -> MigrationRecord IReadOnlyList

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
  abstract member DryRunDown: [<Optional>] ?amount: int -> Migration IReadOnlyList

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
    [<Optional>] ?amount: int * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<IReadOnlyList<MigrationRecord>>

  abstract member RunDownAsync:
    [<Optional>] ?amount: int * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<IReadOnlyList<MigrationRecord>>

  abstract member DryRunUpAsync:
    [<Optional>] ?amount: int * [<Optional>] ?cancellationToken: CancellationToken -> Task<Migration IReadOnlyList>

  abstract member DryRunDownAsync:
    [<Optional>] ?amount: int * [<Optional>] ?cancellationToken: CancellationToken -> Task<Migration IReadOnlyList>

  abstract member MigrationsListAsync:
    [<Optional>] ?cancellationToken: CancellationToken -> Task<MigrationStatus IReadOnlyList>

  abstract member ScriptStatusAsync:
    string * [<Optional>] ?cancellationToken: CancellationToken -> Task<MigrationStatus>


[<Class>]
type Migrondi =
  /// <summary>
  /// Generates a new Migrondi service, this can be further customized by passing in a custom database service
  /// a custom file system service and a custom logger.
  ///
  /// Please keep in mind that both the file system implementation can also be async, not just synchronous
  /// for other use cases.
  /// </summary>
  /// <param name="config">A configuration object to be able to find the connection string for the database.</param>
  /// <param name="database">A database service that can be used to run migrations against the database</param>
  /// <param name="fileSystem">A file system service that can be used to read and write migrations</param>
  /// <param name="logger"></param>
  /// <returns>A new Migrondi service</returns>
  internal new:
    config: MigrondiConfig * database: IMiDatabaseHandler * fileSystem: IMiFileSystem * logger: ILogger -> Migrondi

  static member MigrondiFactory:
    config: MigrondiConfig * rootDirectory: string * ?logger: ILogger<IMigrondi> -> IMigrondi

  interface IMigrondi