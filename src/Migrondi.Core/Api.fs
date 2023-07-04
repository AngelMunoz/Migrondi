[<AutoOpen>]
module Migrondi.Core.Api

open System
open System.Runtime.InteropServices


open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open Migrondi.Core.Migrondi


let rootUri (cwd: string option) =
  Uri(defaultArg cwd Environment.CurrentDirectory, UriKind.Absolute)

let fileSystem serializer cwd =
  FileSystemImpl.BuildDefaultEnv(serializer, rootUri cwd)

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
    [<Optional>] ?serializer: SerializerEnv,
    [<Optional>] ?fs: FileSystemEnv,
    [<Optional>] ?db: DatabaseEnv
  ) =
  let serializer = defaultArg serializer (SerializerImpl.BuildDefaultEnv())
  let fs = defaultArg fs (fileSystem serializer rootUri)
  let config = fs.ReadConfiguration(defaultArg configPath "./migrondi.json")
  let db = defaultArg db (DatabaseImpl.Build(config))

  let migrondi = MigrondiServiceImpl.BuildDefaultEnv(db, fs, config)

  interface MigrondiService with
    member _.DryRunDown([<Optional>] ?amount) =

      migrondi.DryRunDown(?amount = amount)

    member _.DryRunDownAsync
      (
        [<Optional>] ?amount,
        [<Optional>] ?cancellationToken
      ) =
      migrondi.DryRunDownAsync()

    member _.DryRunUp([<Optional>] ?amount: int) =

      migrondi.DryRunUp(?amount = amount)

    member _.DryRunUpAsync
      (
        [<Optional>] ?amount,
        [<Optional>] ?cancellationToken
      ) =
      migrondi.DryRunUpAsync()

    member _.MigrationsList() = migrondi.MigrationsList()

    member _.MigrationsListAsync([<Optional>] ?cancellationToken) =
      migrondi.MigrationsListAsync()

    member _.RunDown([<Optional>] ?amount: int) =

      migrondi.RunDown(?amount = amount)

    member _.RunDownAsync
      (
        [<Optional>] ?amount,
        [<Optional>] ?cancellationToken
      ) =
      migrondi.RunDownAsync()

    member _.RunUp([<Optional>] ?amount: int) =

      migrondi.RunUp(?amount = amount)

    member _.RunUpAsync([<Optional>] ?amount, [<Optional>] ?cancellationToken) =

      migrondi.RunUpAsync()

    member _.ScriptStatus(migrationPath: string) =

      migrondi.ScriptStatus(migrationPath)

    member _.ScriptStatusAsync(arg1: string, [<Optional>] ?cancellationToken) =

      migrondi.ScriptStatusAsync(arg1)

[<RequireQualifiedAccessAttribute>]
module Migrondi =

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
  let defaultMigrondi = Migrondi()

  let inline runUp (amount: int) =
    Api.runDownWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  let inline runAllUp () =
    Api.runUpWithMigrondi defaultMigrondi None |> Seq.toList

  let inline runDown (amount: int) =
    Api.runDownWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  let inline runAllDown () =
    Api.runDownWithMigrondi defaultMigrondi None |> Seq.toList

  let inline dryRunUp (amount: int) =
    Api.dryRunUpWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  let inline dryRunAllUp () =
    Api.dryRunUpWithMigrondi defaultMigrondi None |> Seq.toList

  let inline dryRunDown (amount: int) =
    Api.dryRunDownWithMigrondi defaultMigrondi (Some(amount)) |> Seq.toList

  let inline dryRunAllDown () =
    Api.dryRunDownWithMigrondi defaultMigrondi None |> Seq.toList

  let inline migrationsList () =
    Api.migrationsListWithMigrondi defaultMigrondi |> Seq.toList

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