[<AutoOpen>]
module Migrondi.Core.Api

open System
open System.Runtime.InteropServices


open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open Migrondi.Core.Migrondi


let inline rootUri (cwd: string option) =
  Uri(defaultArg cwd Environment.CurrentDirectory, UriKind.Absolute)

let inline fileSystem serializer cwd =
  FileSystemImpl.BuildDefaultEnv(serializer, rootUri cwd)

[<Interface>]
type MigrondiEnv =

  abstract member Database: DatabaseService
  abstract member FileSystem: FileSystemService
  abstract member Serializer: SerializerService
  abstract member Migrondi: MigrondiService

/// <summary>
/// Migrondi service
/// </summary>
/// <param name="rootUri">
/// Root of the source files or project, it defaults to the Current working directory
/// but it can be any other location, it is used as the root to resolve relative paths
/// </param>
/// <param name="configPath">Path to configuration file relative to the current working directory, defaults to ./migrondi.json</param>
/// <param name="serializer">Serializer Service</param>
/// <param name="fs">
/// FileSystem service, it is used to obtain source files or configuration files as well as writing them to the sources
/// It doesn't have to be a real file system, it can be a network location or a virtual file system
/// </param>
/// <param name="db">
/// Database Service, by default this interfaces with the database specified in the migrondi.json file
/// though the interface allows for other kinds of implementations like network locations or a virtual file system.
/// </param>
/// <remarks>
/// This is the
/// </remarks>
type Migrondi
  (
    [<Optional>] ?rootUri: string,
    [<Optional>] ?configPath: string,
    [<Optional>] ?serializer: SerializerService,
    [<Optional>] ?fs: FileSystemService,
    [<Optional>] ?db: DatabaseService
  ) =
  let serializer = defaultArg serializer (SerializerImpl.BuildDefaultEnv())
  let fs = defaultArg fs (fileSystem serializer rootUri)
  let config = fs.ReadConfiguration(defaultArg configPath "./migrondi.json")
  let db = defaultArg db (DatabaseImpl.Build(config))

  let migrondi = MigrondiServiceImpl.BuildDefaultEnv(db, fs, config)

  interface MigrondiEnv with

    member _.Database: DatabaseService = db

    member _.Serializer: SerializerService = serializer

    member _.FileSystem: FileSystemService = fs

    member _.Migrondi: MigrondiService = migrondi

/// <summary>
/// An F# Flavored Migrondi API that provides a default <see cref="Migrondi.Core.Migrondi.MigrondiService">MigrondiService</see>
/// </summary>
[<RequireQualifiedAccessAttribute>]
module Migrondi =

  /// <summary>
  /// In case you'd need to customize the <see cref="Migrondi.Core.Migrondi.MigrondiService">MigrondiService</see> instance that is used by other
  /// functions in the Migrondi module, you can use the Api functions to provide your own instance of the service
  /// </summary>
  module Api =
    open System.Threading

    let inline runUpWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      =
      migrondi.RunUp(?amount = amount)

    let inline runDownWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      =
      migrondi.RunDown(?amount = amount)

    let inline dryRunUpWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      =
      migrondi.DryRunUp(?amount = amount)

    let inline dryRunDownWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      =
      migrondi.DryRunDown(?amount = amount)

    let inline migrationsListWithMigrondi (migrondi: MigrondiService) =
      migrondi.MigrationsList()

    let inline scriptStatusWithMigrondi
      (migrondi: MigrondiService)
      (migrationPath: string)
      =
      migrondi.ScriptStatus(migrationPath)

    let inline runUpAsyncWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      (cancellationToken: CancellationToken option)
      =
      migrondi.RunUpAsync(
        ?amount = amount,
        ?cancellationToken = cancellationToken
      )

    let inline runDownAsyncWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      (cancellationToken: CancellationToken option)
      =
      migrondi.RunDownAsync(
        ?amount = amount,
        ?cancellationToken = cancellationToken
      )

    let inline dryRunUpAsyncWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      (cancellationToken: CancellationToken option)
      =
      migrondi.DryRunUpAsync(
        ?amount = amount,
        ?cancellationToken = cancellationToken
      )

    let inline dryRunDownAsyncWithMigrondi
      (migrondi: MigrondiService)
      (amount: int option)
      (cancellationToken: CancellationToken option)
      =
      migrondi.DryRunDownAsync(
        ?amount = amount,
        ?cancellationToken = cancellationToken
      )

    let inline migrationsListAsyncWithMigrondi
      (migrondi: MigrondiService)
      (cancellationToken: CancellationToken option)
      =
      migrondi.MigrationsListAsync(?cancellationToken = cancellationToken)

    let inline scriptStatusAsyncWithMigrondi
      (migrondi: MigrondiService)
      (migrationPath: string)
      (cancellationToken: CancellationToken option)
      =
      migrondi.ScriptStatusAsync(
        migrationPath,
        ?cancellationToken = cancellationToken
      )

  [<CompiledNameAttribute "DefaultMigrondi">]
  let defaultMigrondi: MigrondiService = (Migrondi() :> MigrondiEnv).Migrondi

  /// <summary>
  /// Runs a specific amount pending migrations against the database
  /// </summary>
  /// <param name="amount">The amount of migrations to apply</param>
  /// <returns>
  /// A list of all migrations that were applied including previously applied ones
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  let inline runUp (amount: int) =
    Api.runDownWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  /// <summary>
  /// Runs all pending migrations against the database
  /// </summary>
  /// <returns>
  /// A list of all migrations that were applied including previously applied ones
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  let inline runAllUp () =
    Api.runUpWithMigrondi defaultMigrondi None |> Seq.toList

  /// <summary>
  /// Reverts a specific amount of migrations that were previously applied
  /// </summary>
  /// <param name="amount">The amount of migrations to roll back</param>
  /// <returns>
  /// A list of all migrations that were reverted including previously applied ones
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  let inline runDown (amount: int) =
    Api.runDownWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  /// <summary>
  /// Reverts all migrations that were previously applied
  /// </summary>
  /// <returns>
  /// A list of all migrations that were reverted including previously applied ones
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  let inline runAllDown () =
    Api.runDownWithMigrondi defaultMigrondi None |> Seq.toList

  /// <summary>
  /// Makes a list with the amount of the pending migrations that would be applied
  /// </summary>
  /// <param name="amount">The amount of migrations to apply</param>
  /// <returns>
  /// A list of all migrations that would be applied
  /// </returns>
  let inline dryRunUp (amount: int) =
    Api.dryRunUpWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  /// <summary>
  /// Makes a list of the pending migrations that would be applied
  /// </summary>
  /// <returns>
  /// A list of all migrations that would be applied
  /// </returns>
  let inline dryRunAllUp () =
    Api.dryRunUpWithMigrondi defaultMigrondi None |> Seq.toList

  /// <summary>
  /// Makes a list of the pending migrations that would be reverted
  /// </summary>
  /// <param name="amount">The amount of migrations to roll back</param>
  /// <returns>
  /// A list of all migrations that would be reverted
  /// </returns>
  let inline dryRunDown (amount: int) =
    Api.dryRunDownWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  /// <summary>
  /// Makes a list of the pending migrations that would be reverted
  /// </summary>
  /// <returns>
  /// A list of all migrations that would be reverted
  /// </returns>
  let inline dryRunAllDown () =
    Api.dryRunDownWithMigrondi defaultMigrondi None |> Seq.toList

  /// <summary>
  /// Makes a list of all migrations and their status
  /// </summary>
  /// <returns>
  /// A list of all migrations and their status
  /// </returns>
  /// <remarks>
  /// This method coordinates between the source scripts and the database
  /// </remarks>
  let inline migrationsList () =
    Api.migrationsListWithMigrondi defaultMigrondi |> Seq.toList

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
  let inline scriptStatus (migrationPath: string) =
    Api.scriptStatusWithMigrondi defaultMigrondi migrationPath

  let runUpAsync (amount: int) = async {
    let! token = Async.CancellationToken

    let! result =
      Api.runUpAsyncWithMigrondi defaultMigrondi (Some(amount)) (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let runAllUpAsync () = async {
    let! token = Async.CancellationToken

    let! result =
      Api.runUpAsyncWithMigrondi defaultMigrondi None (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let runDownAsync (amount: int) = async {
    let! token = Async.CancellationToken

    let! result =
      Api.runDownAsyncWithMigrondi defaultMigrondi (Some(amount)) (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let runAllDownAsync () = async {
    let! token = Async.CancellationToken

    let! result =
      Api.runDownAsyncWithMigrondi defaultMigrondi None (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let dryRunUpAsync (amount: int) = async {
    let! token = Async.CancellationToken

    let! result =
      Api.dryRunUpAsyncWithMigrondi defaultMigrondi (Some(amount)) (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let dryRunAllUpAsync () = async {
    let! token = Async.CancellationToken

    let! result =
      Api.dryRunUpAsyncWithMigrondi defaultMigrondi None (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let dryRunDownAsync (amount: int) = async {
    let! token = Async.CancellationToken

    let! result =
      Api.dryRunDownAsyncWithMigrondi
        defaultMigrondi
        (Some(amount))
        (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let dryRunAllDownAsync () = async {
    let! token = Async.CancellationToken

    let! result =
      Api.dryRunDownAsyncWithMigrondi defaultMigrondi None (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let migrationsListAsync () = async {
    let! token = Async.CancellationToken

    let! result =
      Api.migrationsListAsyncWithMigrondi defaultMigrondi (Some(token))
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let scriptStatusAsync (migrationPath: string) = async {
    let! token = Async.CancellationToken

    let! result =
      Api.scriptStatusAsyncWithMigrondi
        defaultMigrondi
        migrationPath
        (Some(token))
      |> Async.AwaitTask

    return result
  }