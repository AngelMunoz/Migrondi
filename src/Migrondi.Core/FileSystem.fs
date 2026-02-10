namespace Migrondi.Core.FileSystem

open System
open System.Collections.Generic
open System.IO
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

/// <summary>
/// Minimal abstraction for reading/writing raw migration content.
/// Users implement this to provide custom sources (HTTP, S3, Azure Blob, etc.).
/// The library handles all serialization/deserialization internally.
/// </summary>
[<Interface>]
type IMiMigrationSource =
  abstract member ReadContent: uri: Uri -> string

  abstract member ReadContentAsync:
    uri: Uri * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<string>

  abstract member WriteContent: uri: Uri * content: string -> unit

  abstract member WriteContentAsync:
    uri: Uri *
    content: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  abstract member ListFiles: locationUri: Uri -> Uri seq

  abstract member ListFilesAsync:
    locationUri: Uri * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Uri seq>

module PhysicalMigrationSourceImpl =

  let readContent (logger: ILogger, uri: Uri) =
    logger.LogDebug("Reading content from {Path}", uri.LocalPath)

    try
      File.ReadAllText uri.LocalPath
    with
    | :? DirectoryNotFoundException
    | :? IOException as ex ->
      reriseCustom(
        SourceNotFound(uri.LocalPath |> Path.GetFileName, uri.LocalPath)
      )

  let readContentAsync (logger: ILogger, uri: Uri) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    logger.LogDebug("Reading content from {Path}", uri.LocalPath)

    try
      return! File.ReadAllTextAsync(uri.LocalPath, cancellationToken = token)
    with
    | :? DirectoryNotFoundException
    | :? IOException as ex ->
      return
        reriseCustom(
          SourceNotFound(uri.LocalPath |> Path.GetFileName, uri.LocalPath)
        )
  }

  let writeContent (logger: ILogger, uri: Uri, content: string) =
    logger.LogDebug("Writing content to {Path}", uri.LocalPath)
    let file = FileInfo(uri.LocalPath)
    file.Directory.Create()
    File.WriteAllText(uri.LocalPath, content)

  let writeContentAsync (logger: ILogger, uri: Uri, content: string) = cancellableTask {
    logger.LogDebug("Writing content to {Path}", uri.LocalPath)
    let! token = CancellableTask.getCancellationToken()
    let file = FileInfo(uri.LocalPath)
    file.Directory.Create()

    do!
      File.WriteAllTextAsync(uri.LocalPath, content, cancellationToken = token)
  }

  let listFiles (logger: ILogger, locationUri: Uri) =
    logger.LogDebug("Listing files in {Path}", locationUri.LocalPath)
    let directory = DirectoryInfo(locationUri.LocalPath)

    directory.GetFileSystemInfos()
    |> Seq.choose(fun file ->
      match file.Name with
      | V0Name _
      | V1Name _ -> Some(Uri(file.FullName))
      | _ -> None
    )

  let listFilesAsync (logger: ILogger, locationUri: Uri) = cancellableTask {
    logger.LogDebug("Listing files in {Path}", locationUri.LocalPath)
    let directory = DirectoryInfo(locationUri.LocalPath)

    return
      directory.GetFileSystemInfos()
      |> Seq.choose(fun file ->
        match file.Name with
        | V0Name _
        | V1Name _ -> Some(Uri(file.FullName))
        | _ -> None
      )
  }

  let create (logger: ILogger) =
    { new IMiMigrationSource with
        member _.ReadContent(uri: Uri) = readContent(logger, uri)

        member _.ReadContentAsync(uri: Uri, ?cancellationToken) =
          let token = defaultArg cancellationToken CancellationToken.None
          readContentAsync (logger, uri) token

        member _.WriteContent(uri: Uri, content: string) =
          writeContent(logger, uri, content)

        member _.WriteContentAsync
          (uri: Uri, content: string, ?cancellationToken)
          =
          let token = defaultArg cancellationToken CancellationToken.None
          writeContentAsync (logger, uri, content) token

        member _.ListFiles(locationUri: Uri) = listFiles(logger, locationUri)

        member _.ListFilesAsync(locationUri: Uri, ?cancellationToken) =
          let token = defaultArg cancellationToken CancellationToken.None
          listFilesAsync (logger, locationUri) token
    }

[<Interface>]
type IMiFileSystem =
  abstract member ReadConfiguration: readFrom: string -> MigrondiConfig

  abstract member ReadConfigurationAsync:
    readFrom: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrondiConfig>

  abstract member WriteConfiguration:
    config: MigrondiConfig * writeTo: string -> unit

  abstract member WriteConfigurationAsync:
    config: MigrondiConfig *
    writeTo: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  abstract member ReadMigration: migrationName: string -> Migration

  abstract member ReadMigrationAsync:
    migrationName: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration>

  abstract member WriteMigration:
    migration: Migration * migrationName: string -> unit

  abstract member WriteMigrationAsync:
    migration: Migration *
    migrationName: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  abstract member ListMigrations:
    migrationsLocation: string -> Migration IReadOnlyList

  abstract member ListMigrationsAsync:
    migrationsLocation: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration IReadOnlyList>

module PhysicalFileSystemImpl =

  let readConfiguration
    (
      source: IMiMigrationSource,
      serializer: IMiConfigurationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    let path = Uri(projectRoot, UMX.untag readFrom)

    logger.LogDebug("Reading configuration from {Path}", path.ToString())

    let content = source.ReadContent path

    try
      serializer.Decode content
    with :? DeserializationFailed as ex ->
      reriseCustom(MalformedSource(path.ToString(), ex.Content, ex.Reason))

  let readConfigurationAsync
    (
      source: IMiMigrationSource,
      serializer: IMiConfigurationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      readFrom: string<RelativeUserPath>
    ) =
    cancellableTask {
      let path = Uri(projectRoot, UMX.untag readFrom)
      let! token = CancellableTask.getCancellationToken()
      logger.LogDebug("Reading configuration from {Path}", path.ToString())

      let! contents = source.ReadContentAsync(path, token)

      try
        return serializer.Decode contents
      with :? DeserializationFailed as ex ->
        return
          reriseCustom(MalformedSource(path.ToString(), ex.Content, ex.Reason))
    }

  let writeConfiguration
    (
      source: IMiMigrationSource,
      serializer: IMiConfigurationSerializer,
      logger: ILogger,
      config: MigrondiConfig,
      projectRoot: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    let path = Uri(projectRoot, UMX.untag writeTo)

    logger.LogDebug(
      "Writing configuration to {Path}, constructed from {WriteTo}",
      path.ToString(),
      writeTo
    )

    source.WriteContent(path, serializer.Encode config)

  let writeConfigurationAsync
    (
      source: IMiMigrationSource,
      serializer: IMiConfigurationSerializer,
      logger: ILogger,
      config: MigrondiConfig,
      projectRoot: Uri,
      writeTo: string<RelativeUserPath>
    ) =
    cancellableTask {
      let path = Uri(projectRoot, UMX.untag writeTo)
      let! token = CancellableTask.getCancellationToken()

      logger.LogDebug(
        "Writing configuration to {Path}, constructed from {WriteTo}",
        path.ToString(),
        writeTo
      )

      do! source.WriteContentAsync(path, serializer.Encode config, token)
    }

  let readMigration
    (
      source: IMiMigrationSource,
      serializer: IMiMigrationSerializer,
      logger: ILogger,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    let path = Uri(migrationsDir, UMX.untag migrationName)

    logger.LogDebug(
      "Reading migration from {Path} with name: {MigrationName}",
      path.ToString(),
      migrationName
    )

    let content = source.ReadContent path

    try
      serializer.DecodeText content
    with :? DeserializationFailed as ex ->
      reriseCustom(MalformedSource(path.ToString(), ex.Content, ex.Reason))

  let readMigrationAsync
    (
      source: IMiMigrationSource,
      serializer: IMiMigrationSerializer,
      logger: ILogger,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    cancellableTask {
      let path = Uri(migrationsDir, UMX.untag migrationName)
      let! token = CancellableTask.getCancellationToken()

      logger.LogDebug(
        "Reading migration from {Path} with name: {MigrationName}",
        path.ToString(),
        migrationName
      )

      let! contents = source.ReadContentAsync(path, token)

      try
        return serializer.DecodeText contents
      with :? DeserializationFailed as ex ->
        return
          reriseCustom(MalformedSource(path.ToString(), ex.Content, ex.Reason))
    }

  let writeMigration
    (
      source: IMiMigrationSource,
      serializer: IMiMigrationSerializer,
      logger: ILogger,
      migration: Migration,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    let path = Uri(migrationsDir, UMX.untag migrationName)

    logger.LogDebug(
      "Writing migration to {Path} with directory: {MigrationsDirectory} and name: {MigrationName}",
      path.ToString(),
      migrationsDir.ToString(),
      migrationName
    )

    source.WriteContent(path, serializer.EncodeText migration)

  let writeMigrationAsync
    (
      source: IMiMigrationSource,
      serializer: IMiMigrationSerializer,
      logger: ILogger,
      migration: Migration,
      migrationsDir: Uri,
      migrationName: string<RelativeUserPath>
    ) =
    cancellableTask {
      let path = Uri(migrationsDir, UMX.untag migrationName)
      let! token = CancellableTask.getCancellationToken()

      logger.LogDebug(
        "Writing migration to {Path} with directory: {MigrationsDirectory} and name: {MigrationName}",
        path.ToString(),
        migrationsDir.ToString(),
        migrationName
      )

      do! source.WriteContentAsync(path, serializer.EncodeText migration, token)
    }

  let listMigrations
    (
      source: IMiMigrationSource,
      serializer: IMiMigrationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      migrationsDir: string<RelativeUserDirectoryPath>
    ) =
    let operation = result {
      let path = Uri(projectRoot, UMX.untag migrationsDir)

      logger.LogDebug(
        "Listing migrations from {Path} with directory: {MigrationsDir}",
        path.ToString(),
        migrationsDir
      )

      let files = source.ListFiles path |> Seq.toList

      return!
        files
        |> List.traverseResultA(fun uri ->
          let name = uri.Segments |> Array.last
          let content = source.ReadContent uri

          try
            serializer.DecodeText(content, name) |> Ok
          with :? DeserializationFailed as ex ->
            MalformedSource(name, ex.Reason, ex.Source) |> Error
        )
    }

    match operation with
    | Ok value -> value |> List.sortByDescending(_.timestamp)
    | Error err ->
      raise(AggregateException("Failed to Decode Some Migrations", err))

  let listMigrationsAsync
    (
      source: IMiMigrationSource,
      serializer: IMiMigrationSerializer,
      logger: ILogger,
      projectRoot: Uri,
      migrationsDir: string<RelativeUserDirectoryPath>
    ) =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let path = Uri(projectRoot, UMX.untag migrationsDir)

      logger.LogDebug(
        "Listing migrations from {Path} with directory: {MigrationsDir}",
        path.ToString(),
        migrationsDir
      )

      let! uris = source.ListFilesAsync(path, token)

      let! files =
        let ops =
          uris
          |> Seq.map(fun uri -> async {
            let! token' = Async.CancellationToken
            let name = uri.Segments |> Array.last

            let! content =
              source.ReadContentAsync(uri, cancellationToken = token')
              |> Async.AwaitTask

            return name, content
          })

        Async.Parallel(ops)

      let operation =
        files
        |> Array.toList
        |> List.traverseResultA(fun (name, contents) ->
          try
            serializer.DecodeText(contents, name) |> Ok
          with :? DeserializationFailed as ex ->
            MalformedSource(name, ex.Reason, ex.Source) |> Error
        )

      return
        match operation with
        | Ok value ->
          value |> List.sortByDescending(_.timestamp) :> IReadOnlyList<_>
        | Error err ->
          raise(AggregateException("Failed to Decode Some Migrations", err))
    }

[<Class>]
type internal MiFileSystem
  (
    logger: ILogger,
    configurationSerializer: IMiConfigurationSerializer,
    migrationSerializer: IMiMigrationSerializer,
    projectRootUri: Uri,
    migrationsRootUri: Uri,
    [<Optional>] ?source: IMiMigrationSource
  ) =
  let migrationSource =
    match source with
    | Some source -> source
    | None -> PhysicalMigrationSourceImpl.create logger

  let migrationsWorkingDir = Uri(projectRootUri, migrationsRootUri)

  interface IMiFileSystem with
    member _.ListMigrations(readFrom) =
      PhysicalFileSystemImpl.listMigrations(
        migrationSource,
        migrationSerializer,
        logger,
        projectRootUri,
        UMX.tag readFrom
      )

    member _.ListMigrationsAsync(arg1, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      PhysicalFileSystemImpl.listMigrationsAsync
        (migrationSource,
         migrationSerializer,
         logger,
         projectRootUri,
         UMX.tag arg1)
        token

    member _.ReadConfiguration(readFrom) =
      PhysicalFileSystemImpl.readConfiguration(
        migrationSource,
        configurationSerializer,
        logger,
        projectRootUri,
        UMX.tag readFrom
      )

    member _.ReadConfigurationAsync(readFrom, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      PhysicalFileSystemImpl.readConfigurationAsync
        (migrationSource,
         configurationSerializer,
         logger,
         projectRootUri,
         UMX.tag readFrom)
        token

    member _.ReadMigration readFrom =
      PhysicalFileSystemImpl.readMigration(
        migrationSource,
        migrationSerializer,
        logger,
        migrationsWorkingDir,
        UMX.tag readFrom
      )

    member _.ReadMigrationAsync(readFrom, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      PhysicalFileSystemImpl.readMigrationAsync
        (migrationSource,
         migrationSerializer,
         logger,
         migrationsWorkingDir,
         UMX.tag readFrom)
        token

    member _.WriteConfiguration(config: MigrondiConfig, writeTo) : unit =
      PhysicalFileSystemImpl.writeConfiguration(
        migrationSource,
        configurationSerializer,
        logger,
        config,
        projectRootUri,
        UMX.tag writeTo
      )

    member _.WriteConfigurationAsync
      (config: MigrondiConfig, writeTo, [<Optional>] ?cancellationToken)
      =
      let token = defaultArg cancellationToken CancellationToken.None

      PhysicalFileSystemImpl.writeConfigurationAsync
        (migrationSource,
         configurationSerializer,
         logger,
         config,
         projectRootUri,
         UMX.tag writeTo)
        token

    member _.WriteMigration(arg1: Migration, arg2) : unit =
      PhysicalFileSystemImpl.writeMigration(
        migrationSource,
        migrationSerializer,
        logger,
        arg1,
        migrationsWorkingDir,
        UMX.tag arg2
      )

    member _.WriteMigrationAsync
      (arg1: Migration, arg2, [<Optional>] ?cancellationToken)
      =
      let token = defaultArg cancellationToken CancellationToken.None

      PhysicalFileSystemImpl.writeMigrationAsync
        (migrationSource,
         migrationSerializer,
         logger,
         arg1,
         migrationsWorkingDir,
         UMX.tag arg2)
        token