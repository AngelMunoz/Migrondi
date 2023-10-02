namespace Migrondi.Core.FileSystem

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

open Microsoft.Extensions.Logging

open FSharp.UMX

open FsToolkit.ErrorHandling

open Migrondi.Core
open Migrondi.Core.Serialization

open IcedTasks

module Units =

  [<Measure>]
  type RelativeUserPath

  [<Measure>]
  type RelativeUserDirectoryPath


open Units


[<Interface>]
type FileSystemService =

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
  /// <param name="migrationName">A path Relative to the RootPath that targets to the migration</param>
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
  abstract member ReadMigration: migrationName: string -> Migration

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="migrationName">A path Relative to the RootPath that targets to the migration</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  ///  Thrown when the file is not found
  /// </exception>
  abstract member ReadMigrationAsync:
    migrationName: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration>

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="migration">The migration object</param>
  /// <param name="migrationName">The path to the migration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigration:
    migration: Migration * migrationName: string -> unit

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="migration">The migration object</param>
  /// <param name="migrationName">The path to the migration file</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigrationAsync:
    migration: Migration *
    migrationName: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="migrationsLocation">A path Relative to the RootPath that targets to the migration.</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="System.AggregateException">
  /// A list of exceptions in case the sources were not readable or malformed
  /// This normally includes exceptions of the type <see cref="Migrondi.Core.FileSystem.MalformedSource">MalformedSource</see>
  /// </exception>
  abstract member ListMigrations:
    migrationsLocation: string -> Migration IReadOnlyList

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="migrationsLocation">A path Relative to the RootPath that targets to the migration</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ListMigrationsAsync:
    migrationsLocation: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration IReadOnlyList>



module PhysicalFileSystemImpl =

  let nameSchema = Regex(MigrationNameSchema)

  let readConfiguration
    (
      serializer: ConfigurationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    let path = Uri(projectRoot, UMX.untag readFrom)

    logger.LogDebug("Reading configuration from {Path}", path.LocalPath)

    let content =
      try
        File.ReadAllText path.LocalPath
      with
      | :? DirectoryNotFoundException
      | :? IOException as ex ->
        reriseCustom(
          SourceNotFound(path.LocalPath |> Path.GetFileName, path.LocalPath)
        )

    try
      serializer.Decode content
    with :? DeserializationFailed as ex ->
      reriseCustom(MalformedSource(path.LocalPath, ex.Content, ex.Reason))

  let readConfigurationAsync
    (
      serializer: ConfigurationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    cancellableTask {
      let path = Uri(projectRoot, UMX.untag readFrom)
      logger.LogDebug("Reading configuration from {Path}", path.LocalPath)

      let! contents =
        fun token -> task {
          try
            return!
              File.ReadAllTextAsync(path.LocalPath, cancellationToken = token)
          with
          | :? DirectoryNotFoundException
          | :? IOException ->
            return
              reriseCustom(
                SourceNotFound(
                  path.LocalPath |> Path.GetFileName,
                  path.LocalPath
                )
              )
        }

      try
        return serializer.Decode contents
      with :? DeserializationFailed as ex ->
        return
          reriseCustom(MalformedSource(path.LocalPath, ex.Content, ex.Reason))
    }

  let writeConfiguration
    (
      serializer: ConfigurationSerializer,
      logger: ILogger,
      config: MigrondiConfig,
      projectRoot: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    let path = Uri(projectRoot, UMX.untag writeTo)
    let file = FileInfo(path.LocalPath)

    logger.LogDebug(
      "Writing configuration to {Path}, constructed fom {WriteTo}",
      path.LocalPath,
      writeTo
    )

    file.Directory.Create()

    File.WriteAllText(path.LocalPath, serializer.Encode config)

  let writeConfigurationAsync
    (
      serializer: ConfigurationSerializer,
      logger: ILogger,
      config: MigrondiConfig,
      projectRoot: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let path = Uri(projectRoot, UMX.untag writeTo)
      let file = FileInfo(path.LocalPath)

      logger.LogDebug(
        "Writing configuration to {Path}, constructed fom {WriteTo}",
        path.LocalPath,
        writeTo
      )

      file.Directory.Create()

      do!
        File.WriteAllTextAsync(
          path.LocalPath,
          serializer.Encode config,
          cancellationToken = token
        )
    }

  let readMigration
    (
      serializer: MigrationSerializer,
      logger: ILogger,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    let path = Uri(migrationsDir, UMX.untag migrationName)

    logger.LogDebug(
      "Reading migration from {Path} with name: {MigrationName}",
      path.LocalPath,
      migrationName
    )

    let content =
      try
        File.ReadAllText path.LocalPath
      with
      | :? DirectoryNotFoundException
      | :? IOException as ex ->
        reriseCustom(
          SourceNotFound(path.LocalPath |> Path.GetFileName, path.LocalPath)
        )

    try
      serializer.DecodeText content
    with :? DeserializationFailed as ex ->
      reriseCustom(MalformedSource(path.LocalPath, ex.Content, ex.Reason))

  let readMigrationAsync
    (
      serializer: MigrationSerializer,
      logger: ILogger,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    cancellableTask {
      let path = Uri(migrationsDir, UMX.untag migrationName)

      logger.LogDebug(
        "Reading migration from {Path} with name: {MigrationName}",
        path.LocalPath,
        migrationName
      )

      let! contents =
        fun token -> task {
          try
            return!
              File.ReadAllTextAsync(path.LocalPath, cancellationToken = token)
          with
          | :? DirectoryNotFoundException
          | :? IOException ->
            return
              reriseCustom(
                SourceNotFound(
                  path.LocalPath |> Path.GetFileName,
                  path.LocalPath
                )
              )
        }

      try
        return serializer.DecodeText contents
      with :? DeserializationFailed as ex ->
        return
          reriseCustom(MalformedSource(path.LocalPath, ex.Content, ex.Reason))
    }

  let writeMigration
    (
      serializer: MigrationSerializer,
      logger: ILogger,
      migration: Migration,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    let path = Uri(migrationsDir, UMX.untag migrationName)

    logger.LogDebug(
      "Writing migration to {Path} with directory: {MigrationsDirectory} and name: {MigrationName}",
      path.LocalPath,
      migrationsDir.LocalPath,
      migrationName
    )

    let file = FileInfo(path.LocalPath)
    file.Directory.Create()

    File.WriteAllText(path.LocalPath, serializer.EncodeText migration)

  let writeMigrationAsync
    (
      serializer: MigrationSerializer,
      logger: ILogger,
      migration: Migration,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let path = Uri(migrationsDir, UMX.untag migrationName)

      logger.LogDebug(
        "Writing migration to {Path} with directory: {MigrationsDirectory} and name: {MigrationName}",
        path.LocalPath,
        migrationsDir.LocalPath,
        migrationName
      )

      let file = FileInfo(path.LocalPath)
      file.Directory.Create()

      do!
        File.WriteAllTextAsync(
          path.LocalPath,
          serializer.EncodeText migration,
          cancellationToken = token
        )
    }

  let listMigrations
    (
      serializer: MigrationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      migrationsDir: string<RelativeUserDirectoryPath>
    ) =
    let operation = result {
      let path = Uri(projectRoot, UMX.untag migrationsDir)

      logger.LogDebug(
        "Listing migrations from {Path} with directory: {MigrationsDir}",
        path.LocalPath,
        migrationsDir
      )

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
          try
            serializer.DecodeText(contents, name) |> Ok
          with :? DeserializationFailed as ex ->
            MalformedSource(name, ex.Reason, ex.Source) |> Error
        )
    }

    match operation with
    | Ok value ->
      value |> List.sortByDescending(fun migration -> migration.timestamp)
    | Error err ->
      raise(AggregateException("Failed to Decode Some Migrations", err))

  let listMigrationsAsync
    (
      serializer: MigrationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      migrationsDir: string<RelativeUserDirectoryPath>
    ) =
    cancellableTask {
      let! operation = taskResult {
        let path = Uri(projectRoot, UMX.untag migrationsDir)

        logger.LogDebug(
          "Listing migrations from {Path} with directory: {MigrationsDir}",
          path.LocalPath,
          migrationsDir
        )

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
            try
              serializer.DecodeText(contents, name) |> Ok
            with :? DeserializationFailed as ex ->
              MalformedSource(name, ex.Reason, ex.Source) |> Error
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
type FileSystemServiceFactory =

  static member GetInstance
    (
      projectRootUri: Uri,
      migrationsRootUri: Uri,
      logger: ILogger,
      [<Optional>] ?configurationSerializer: ConfigurationSerializer,
      [<Optional>] ?migrationSerializer: MigrationSerializer
    ) =
    let configSerializer =
      defaultArg
        configurationSerializer
        (SerializationFactory.GetConfigurationSerializer())

    let migrationSerializer =
      defaultArg
        migrationSerializer
        (SerializationFactory.GetMigrationSerializer())

    let migrationsWorkingDir = Uri(projectRootUri, migrationsRootUri)

    { new FileSystemService with
        member _.ListMigrations(readFrom) =
          PhysicalFileSystemImpl.listMigrations(
            migrationSerializer,
            logger,
            projectRootUri,
            UMX.tag readFrom
          )

        member _.ListMigrationsAsync(arg1, [<Optional>] ?cancellationToken) =
          let token = defaultArg cancellationToken CancellationToken.None

          PhysicalFileSystemImpl.listMigrationsAsync
            (migrationSerializer, logger, projectRootUri, UMX.tag arg1)
            token

        member _.ReadConfiguration(readFrom) =
          PhysicalFileSystemImpl.readConfiguration(
            configSerializer,
            logger,
            projectRootUri,
            UMX.tag readFrom
          )

        member _.ReadConfigurationAsync
          (
            readFrom,
            [<Optional>] ?cancellationToken
          ) =
          let token = defaultArg cancellationToken CancellationToken.None

          PhysicalFileSystemImpl.readConfigurationAsync
            (configSerializer, logger, projectRootUri, UMX.tag readFrom)
            token

        member _.ReadMigration readFrom =
          PhysicalFileSystemImpl.readMigration(
            migrationSerializer,
            logger,
            migrationsWorkingDir,
            UMX.tag readFrom
          )

        member _.ReadMigrationAsync(readFrom, [<Optional>] ?cancellationToken) =
          let token = defaultArg cancellationToken CancellationToken.None

          PhysicalFileSystemImpl.readMigrationAsync
            (migrationSerializer, logger, migrationsWorkingDir, UMX.tag readFrom)
            token

        member _.WriteConfiguration(config: MigrondiConfig, writeTo) : unit =
          PhysicalFileSystemImpl.writeConfiguration(
            configSerializer,
            logger,
            config,
            projectRootUri,
            UMX.tag writeTo
          )

        member _.WriteConfigurationAsync
          (
            config: MigrondiConfig,
            writeTo,
            [<Optional>] ?cancellationToken
          ) =
          let token = defaultArg cancellationToken CancellationToken.None

          PhysicalFileSystemImpl.writeConfigurationAsync
            (configSerializer, logger, config, projectRootUri, UMX.tag writeTo)
            token

        member _.WriteMigration(arg1: Migration, arg2) : unit =
          PhysicalFileSystemImpl.writeMigration(
            migrationSerializer,
            logger,
            arg1,
            migrationsWorkingDir,
            UMX.tag arg2
          )

        member _.WriteMigrationAsync
          (
            arg1: Migration,
            arg2,
            [<Optional>] ?cancellationToken
          ) =
          let token = defaultArg cancellationToken CancellationToken.None

          PhysicalFileSystemImpl.writeMigrationAsync
            (migrationSerializer,
             logger,
             arg1,
             migrationsWorkingDir,
             UMX.tag arg2)
            token
    }