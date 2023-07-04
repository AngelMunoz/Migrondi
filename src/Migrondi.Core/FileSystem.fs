namespace Migrondi.Core.FileSystem

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

open FSharp.UMX

open FsToolkit.ErrorHandling

open Migrondi.Core
open Migrondi.Core.Serialization

module Units =

  [<Measure>]
  type RelativeUserPath

  [<Measure>]
  type RelativeUserDirectoryPath


open Units


[<Interface>]
type FileSystemEnv =

  /// <summary>
  /// Take the path to a configuration source, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <returns>A <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object</returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the source is not found
  /// </exception>
  /// <exception cref="Migrondi.Core.FileSystem.MalformedSource">
  /// Thrown when the source is found but can't be deserialized from disk
  /// </exception>
  abstract member ReadConfiguration: readFrom: string -> MigrondiConfig

  /// <summary>
  /// Take the path to a configuration source, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the file is not found
  /// </exception>
  /// <exception cref="Migrondi.Core.FileSystem.MalformedSource">
  /// Thrown when the file is found but can't be deserialized from the source
  /// </exception>
  abstract member ReadConfigurationAsync:
    readFrom: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrondiConfig>

  /// <summary>
  /// Take a configuration object and writes it to a location dictated by the `writeTo` parameter
  /// </summary>
  /// <param name="config">The configuration object</param>
  /// <param name="writeTo">The path to the configuration file</param>
  abstract member WriteConfiguration:
    config: MigrondiConfig * writeTo: string -> unit

  /// <summary>
  /// Take a configuration object and writes it to a file
  /// </summary>
  /// <param name="config">The configuration object</param>
  /// <param name="writeTo">The path to the configuration file</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member WriteConfigurationAsync:
    config: MigrondiConfig *
    writeTo: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the file is not found
  /// </exception>
  /// <exception cref="Migrondi.Core.FileSystem.MalformedSource">
  /// Thrown when the file is found but can't be deserialized from the source
  /// </exception>
  abstract member ReadMigration: readFrom: string -> Migration

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  ///  Thrown when the file is not found
  /// </exception>
  abstract member ReadMigrationAsync:
    readFrom: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration>

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="migration">The migration object</param>
  /// <param name="writeTo">The path to the migration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigration: migration: Migration * writeTo: string -> unit

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="migration">The migration object</param>
  /// <param name="writeTo">The path to the migration file</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigrationAsync:
    migration: Migration *
    writeTo: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration.</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="System.AggregateException">
  /// A list of exceptions in case the sources were not readable or malformed
  /// This normally includes exceptions of the type <see cref="Migrondi.Core.FileSystem.MalformedSource">MalformedSource</see>
  /// </exception>
  abstract member ListMigrations: readFrom: string -> Migration IReadOnlyList

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ListMigrationsAsync:
    readFrom: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration IReadOnlyList>

module PhysicalFileSystemImpl =
  open System.IO

  let nameSchema = Regex("(.+)_([0-9]+).(sql|SQL)")

  let readConfiguration
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    let path = Uri(root, UMX.untag readFrom)

    let content =
      try
        File.ReadAllText path.LocalPath
      with
      | :? DirectoryNotFoundException
      | :? IOException as ex ->
        reriseCustom(
          SourceNotFound(path.LocalPath |> Path.GetFileName, path.LocalPath)
        )

    match serializer.ConfigurationSerializer.Decode content with
    | Ok value -> value
    | Error err -> raise(MalformedSource(path.LocalPath, err))

  let readConfigurationAsync
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    async {
      let! token = Async.CancellationToken
      let path = Uri(root, UMX.untag readFrom)

      let! contents = async {
        try
          return!
            File.ReadAllTextAsync(path.LocalPath, cancellationToken = token)
            |> Async.AwaitTask
        with
        | :? DirectoryNotFoundException
        | :? IOException ->
          return
            reriseCustom(
              SourceNotFound(path.LocalPath |> Path.GetFileName, path.LocalPath)
            )
      }

      return
        match serializer.ConfigurationSerializer.Decode contents with
        | Ok value -> value
        | Error err -> raise(MalformedSource(path.LocalPath, err))
    }

  let writeConfiguration
    (
      serializer: #SerializerEnv,
      config: MigrondiConfig,
      root: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    let path = Uri(root, UMX.untag writeTo)
    let file = FileInfo(path.LocalPath)

    file.Directory.Create()

    File.WriteAllText(
      path.LocalPath,
      serializer.ConfigurationSerializer.Encode config
    )

  let writeConfigurationAsync
    (
      serializer: #SerializerEnv,
      config: MigrondiConfig,
      root: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    async {
      let! token = Async.CancellationToken
      let path = Uri(root, UMX.untag writeTo)
      let file = FileInfo(path.LocalPath)

      file.Directory.Create()

      do!
        File.WriteAllTextAsync(
          path.LocalPath,
          serializer.ConfigurationSerializer.Encode config,
          cancellationToken = token
        )
        |> Async.AwaitTask
    }

  let readMigration
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    let path = Uri(root, UMX.untag readFrom)

    let content =
      try
        File.ReadAllText path.LocalPath
      with
      | :? DirectoryNotFoundException
      | :? IOException as ex ->
        reriseCustom(
          SourceNotFound(path.LocalPath |> Path.GetFileName, path.LocalPath)
        )

    match serializer.MigrationSerializer.DecodeText content with
    | Ok value -> value
    | Error err -> raise(MalformedSource(path.LocalPath, err))

  let readMigrationAsync
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    async {
      let! token = Async.CancellationToken
      let path = Uri(root, UMX.untag readFrom)

      let! contents = async {
        try
          return!
            File.ReadAllTextAsync(path.LocalPath, cancellationToken = token)
            |> Async.AwaitTask
        with
        | :? DirectoryNotFoundException
        | :? IOException ->
          return
            reriseCustom(
              SourceNotFound(path.LocalPath |> Path.GetFileName, path.LocalPath)
            )
      }

      return
        match serializer.MigrationSerializer.DecodeText contents with
        | Ok value -> value
        | Error err -> raise(MalformedSource(path.LocalPath, err))
    }

  let writeMigration
    (
      serializer: #SerializerEnv,
      migration: Migration,
      root: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    let path = Uri(root, UMX.untag writeTo)
    let file = FileInfo(path.LocalPath)

    file.Directory.Create()

    File.WriteAllText(
      path.LocalPath,
      serializer.MigrationSerializer.EncodeText migration
    )

  let writeMigrationAsync
    (
      serializer: #SerializerEnv,
      migration: Migration,
      root: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    async {
      let! token = Async.CancellationToken
      let path = Uri(root, UMX.untag writeTo)
      let file = FileInfo(path.LocalPath)

      file.Directory.Create()

      do!
        File.WriteAllTextAsync(
          path.LocalPath,
          serializer.MigrationSerializer.EncodeText migration,
          cancellationToken = token
        )
        |> Async.AwaitTask
    }

  let listMigrations
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserDirectoryPath>
    ) =
    let operation = result {
      let path = Uri(root, UMX.untag readFrom)

      let directory = DirectoryInfo(path.LocalPath)

      let files =
        directory.GetFileSystemInfos()
        |> Array.Parallel.choose(fun file ->
          if (nameSchema.IsMatch(file.Name)) then
            Some(file.Name, file.FullName |> File.ReadAllText)
          else
            None
        )
        |> Array.toList

      return!
        files
        |> List.traverseResultA(fun (name, contents) ->
          match serializer.MigrationSerializer.DecodeText(contents, name) with
          | Ok migration -> Ok migration
          | Error err -> Error(MalformedSource(name, err))
        )
    }

    match operation with
    | Ok value ->
      value |> List.sortByDescending(fun migration -> migration.timestamp)
    | Error err ->
      raise(AggregateException("Failed to Decode Some Migrations", err))

  let listMigrationsAsync
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserDirectoryPath>
    ) =
    async {
      let! operation = asyncResult {
        let path = Uri(root, UMX.untag readFrom)

        let directory = DirectoryInfo(path.LocalPath)

        let! files =
          directory.GetFileSystemInfos()
          |> Array.Parallel.choose(fun file ->
            if (nameSchema.IsMatch(file.Name)) then Some file else None
          )
          |> Array.Parallel.map(fun file -> async {
            let! token = Async.CancellationToken
            let name = file.Name

            let! content =
              File.ReadAllTextAsync(file.FullName, cancellationToken = token)
              |> Async.AwaitTask

            return name, content
          })
          |> Async.Parallel

        return!
          files
          |> Array.toList
          |> List.traverseResultA(fun (name, contents) ->
            match
              serializer.MigrationSerializer.DecodeText(contents, name)
            with
            | Ok migration -> Ok migration
            | Error err -> Error(MalformedSource(name, err))
          )
      }

      return
        match operation with
        | Ok value ->
          value |> List.sortByDescending(fun migration -> migration.timestamp)
          :> IReadOnlyList<_>
        | Error err ->
          raise(AggregateException("Failed to Decode Some Migrations", err))
    }

[<Class>]
type FileSystemImpl =

  static member BuildDefaultEnv(serializer: #SerializerEnv, rootUri: Uri) =
    { new FileSystemEnv with
        member _.ListMigrations(readFrom) =
          PhysicalFileSystemImpl.listMigrations(
            serializer,
            rootUri,
            UMX.tag readFrom
          )

        member _.ListMigrationsAsync(arg1, [<Optional>] ?cancellationToken) =
          let computation =
            PhysicalFileSystemImpl.listMigrationsAsync(
              serializer,
              rootUri,
              UMX.tag arg1
            )

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

        member _.ReadConfiguration(readFrom) =
          PhysicalFileSystemImpl.readConfiguration(
            serializer,
            rootUri,
            UMX.tag readFrom
          )

        member _.ReadConfigurationAsync
          (
            readFrom,
            [<Optional>] ?cancellationToken
          ) =
          let computation =
            PhysicalFileSystemImpl.readConfigurationAsync(
              serializer,
              rootUri,
              UMX.tag readFrom
            )

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

        member _.ReadMigration readFrom =
          PhysicalFileSystemImpl.readMigration(
            serializer,
            rootUri,
            UMX.tag readFrom
          )

        member _.ReadMigrationAsync(readFrom, [<Optional>] ?cancellationToken) =
          let computation =
            PhysicalFileSystemImpl.readMigrationAsync(
              serializer,
              rootUri,
              UMX.tag readFrom
            )

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

        member _.WriteConfiguration(config: MigrondiConfig, writeTo) : unit =
          PhysicalFileSystemImpl.writeConfiguration(
            serializer,
            config,
            rootUri,
            UMX.tag writeTo
          )

        member _.WriteConfigurationAsync
          (
            config: MigrondiConfig,
            writeTo,
            [<Optional>] ?cancellationToken
          ) =
          let comptation =
            PhysicalFileSystemImpl.writeConfigurationAsync(
              serializer,
              config,
              rootUri,
              UMX.tag writeTo
            )

          Async.StartAsTask(comptation, ?cancellationToken = cancellationToken)

        member _.WriteMigration(arg1: Migration, arg2) : unit =
          PhysicalFileSystemImpl.writeMigration(
            serializer,
            arg1,
            rootUri,
            UMX.tag arg2
          )

        member _.WriteMigrationAsync
          (
            arg1: Migration,
            arg2,
            [<Optional>] ?cancellationToken
          ) =
          let computation =
            PhysicalFileSystemImpl.writeMigrationAsync(
              serializer,
              arg1,
              rootUri,
              UMX.tag arg2
            )

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)
    }