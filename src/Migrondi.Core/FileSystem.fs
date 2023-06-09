namespace Migrondi.Core.FileSystem

open System

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

[<Struct>]
type ReadFileError =
  | FileNotFound of filepath: string * filename: string
  | Malformedfile of
    malformedFileName: string *
    serializationError: SerializationError

[<Interface>]
type FileSystemEnv =

  /// <summary>
  /// Represents the root of the project where the migrondi.json file is located.
  /// </summary>
  /// <remarks>
  /// For file system/web request operations this should be an absolute path to the migrondi.json file
  /// </remarks>
  abstract member RootPath: Uri


  /// <summary>
  /// Take the path to a configuration file, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="serializer">A serializer that will be used to decode the string content from the configuration</param>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <returns>A <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object</returns>
  abstract member ReadConfiguration:
    serializer: #SerializerEnv * readFrom: string<RelativeUserPath> ->
      Result<MigrondiConfig, ReadFileError>

  /// <summary>
  /// Take the path to a configuration file, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="serializer">A serializer that will be used to decode the string content from the configuration</param>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ReadConfigurationAsync:
    serializer: #SerializerEnv * readFrom: string<RelativeUserPath> ->
      Async<Result<MigrondiConfig, ReadFileError>>

  /// <summary>
  /// Take a configuration object and writes it to a location dictated by the `writeTo` parameter
  /// </summary>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="config">The configuration object</param>
  /// <param name="writeTo">The path to the configuration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member WriteConfiguration:
    serializer: #SerializerEnv *
    config: MigrondiConfig *
    writeTo: string<RelativeUserPath> ->
      unit

  /// <summary>
  /// Take a configuration object and writes it to a file
  /// </summary>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="config">The configuration object</param>
  /// <param name="writeTo">The path to the configuration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member WriteConfigurationAsync:
    serializer: #SerializerEnv *
    config: MigrondiConfig *
    writeTo: string<RelativeUserPath> ->
      Async<unit>

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ReadMigration:
    serializer: #SerializerEnv * readFrom: string<RelativeUserPath> ->
      Result<Migration, ReadFileError>

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ReadMigrationAsync:
    serializer: #SerializerEnv * readFrom: string<RelativeUserPath> ->
      Async<Result<Migration, ReadFileError>>

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="migration">The migration object</param>
  /// <param name="writeTo">The path to the migration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigration:
    serializer: #SerializerEnv * Migration * string<RelativeUserPath> -> unit

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="migration">The migration object</param>
  /// <param name="writeTo">The path to the migration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigrationAsync:
    serializer: #SerializerEnv * Migration * string<RelativeUserPath> ->
      Async<unit>

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ListMigrations:
    serializer: #SerializerEnv * readFrom: string<RelativeUserDirectoryPath> ->
      Result<Migration list, ReadFileError list>

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ListMigrationsAsync:
    serializer: #SerializerEnv * string<RelativeUserDirectoryPath> ->
      Async<Result<Migration list, ReadFileError list>>

module PhysicalFileSystemImpl =
  open System.IO

  let readConfiguration
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    let path = Uri(root, UMX.untag readFrom)

    path.LocalPath
    |> File.ReadAllText
    |> serializer.ConfigurationSerializer.Decode
    |> Result.mapError(fun error -> Malformedfile(path.LocalPath, error))

  let readConfigurationAsync
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    async {

      let path = Uri(root, UMX.untag readFrom)
      let! contents = path.LocalPath |> File.ReadAllTextAsync |> Async.AwaitTask

      return
        serializer.ConfigurationSerializer.Decode contents
        |> Result.mapError(fun error -> Malformedfile(path.LocalPath, error))
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
    let path = Uri(root, UMX.untag writeTo)
    let file = FileInfo(path.LocalPath)

    file.Directory.Create()

    File.WriteAllTextAsync(
      path.LocalPath,
      serializer.ConfigurationSerializer.Encode config
    )
    |> Async.AwaitTask

  let readMigration
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    let path = Uri(root, UMX.untag readFrom)

    path.LocalPath
    |> File.ReadAllText
    |> serializer.MigrationSerializer.DecodeText
    |> Result.mapError(fun error -> Malformedfile(path.LocalPath, error))

  let readMigrationAsync
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    async {
      let path = Uri(root, UMX.untag readFrom)
      let! contents = path.LocalPath |> File.ReadAllTextAsync |> Async.AwaitTask

      return
        serializer.MigrationSerializer.DecodeText contents
        |> Result.mapError(fun error -> Malformedfile(path.LocalPath, error))
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
    let path = Uri(root, UMX.untag writeTo)
    let file = FileInfo(path.LocalPath)

    file.Directory.Create()

    File.WriteAllTextAsync(
      path.LocalPath,
      serializer.MigrationSerializer.EncodeText migration
    )
    |> Async.AwaitTask

  let listMigrations
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserDirectoryPath>
    ) =
    result {
      let path = Uri(root, UMX.untag readFrom)

      let directory = DirectoryInfo(path.LocalPath)

      let files =
        directory.GetFileSystemInfos()
        |> Array.Parallel.map(fun file ->
          file.Name, file.FullName |> File.ReadAllText
        )
        |> Array.toList

      return!
        files
        |> List.traverseResultA(fun (name, contents) ->
          match serializer.MigrationSerializer.DecodeText(contents, name) with
          | Ok migration -> Ok migration
          | Error err -> Error(Malformedfile(name, err))
        )
    }

  let listMigrationsAsync
    (
      serializer: #SerializerEnv,
      root: Uri,
      readFrom: string<RelativeUserDirectoryPath>
    ) =
    asyncResult {
      let path = Uri(root, UMX.untag readFrom)

      let directory = DirectoryInfo(path.LocalPath)

      let! files =
        directory.GetFileSystemInfos()
        |> Array.Parallel.map(fun file -> async {
          let name = file.Name

          let! content =
            file.FullName |> File.ReadAllTextAsync |> Async.AwaitTask

          return name, content
        })
        |> Async.Parallel



      return!
        files
        |> Array.toList
        |> List.traverseResultA(fun (name, contents) ->
          match serializer.MigrationSerializer.DecodeText(contents, name) with
          | Ok migration -> Ok migration
          | Error err -> Error(Malformedfile(name, err))
        )
    }

[<Class>]
type FileSystemImpl =

  static member BuildDefaultEnv(?rootUri: Uri) =

    { new FileSystemEnv with
        member _.RootPath: Uri =
          rootUri
          |> Option.defaultWith(fun () ->
            IO.Directory.GetCurrentDirectory() |> Uri
          )

        member this.ListMigrations
          (
            serializer: #SerializerEnv,
            readFrom: string<RelativeUserDirectoryPath>
          ) : Result<Migration list, ReadFileError list> =
          PhysicalFileSystemImpl.listMigrations(
            serializer,
            this.RootPath,
            readFrom
          )

        member this.ListMigrationsAsync
          (
            serializer: #SerializerEnv,
            arg1: string<RelativeUserDirectoryPath>
          ) : Async<Result<Migration list, ReadFileError list>> =
          PhysicalFileSystemImpl.listMigrationsAsync(
            serializer,
            this.RootPath,
            arg1
          )

        member this.ReadConfiguration
          (
            serializer: #SerializerEnv,
            readFrom: string<RelativeUserPath>
          ) : Result<MigrondiConfig, ReadFileError> =
          PhysicalFileSystemImpl.readConfiguration(
            serializer,
            this.RootPath,
            readFrom
          )

        member this.ReadConfigurationAsync
          (
            serializer: #SerializerEnv,
            readFrom: string<RelativeUserPath>
          ) : Async<Result<MigrondiConfig, ReadFileError>> =
          PhysicalFileSystemImpl.readConfigurationAsync(
            serializer,
            this.RootPath,
            readFrom
          )

        member this.ReadMigration
          (
            serializer: #SerializerEnv,
            readFrom: string<RelativeUserPath>
          ) : Result<Migration, ReadFileError> =
          PhysicalFileSystemImpl.readMigration(
            serializer,
            this.RootPath,
            readFrom
          )

        member this.ReadMigrationAsync
          (
            serializer: #SerializerEnv,
            readFrom: string<RelativeUserPath>
          ) : Async<Result<Migration, ReadFileError>> =
          PhysicalFileSystemImpl.readMigrationAsync(
            serializer,
            this.RootPath,
            readFrom
          )

        member this.WriteConfiguration
          (
            serializer: #SerializerEnv,
            config: MigrondiConfig,
            writeTo: string<RelativeUserPath>
          ) : unit =
          PhysicalFileSystemImpl.writeConfiguration(
            serializer,
            config,
            this.RootPath,
            writeTo
          )

        member this.WriteConfigurationAsync
          (
            serializer: #SerializerEnv,
            config: MigrondiConfig,
            writeTo: string<RelativeUserPath>
          ) : Async<unit> =
          PhysicalFileSystemImpl.writeConfigurationAsync(
            serializer,
            config,
            this.RootPath,
            writeTo
          )

        member this.WriteMigration
          (
            serializer: #SerializerEnv,
            arg1: Migration,
            arg2: string<RelativeUserPath>
          ) : unit =
          PhysicalFileSystemImpl.writeMigration(
            serializer,
            arg1,
            this.RootPath,
            arg2
          )

        member this.WriteMigrationAsync
          (
            serializer: #SerializerEnv,
            arg1: Migration,
            arg2: string<RelativeUserPath>
          ) : Async<unit> =
          PhysicalFileSystemImpl.writeMigrationAsync(
            serializer,
            arg1,
            this.RootPath,
            arg2
          )
    }