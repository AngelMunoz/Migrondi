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
type internal IMiFileSystem =
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
      serializer: IMiConfigurationSerializer,
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
      serializer: IMiConfigurationSerializer,
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
      serializer: IMiConfigurationSerializer,
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
      serializer: IMiConfigurationSerializer,
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
      serializer: IMiMigrationSerializer,
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
      serializer: IMiMigrationSerializer,
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
      serializer: IMiMigrationSerializer,
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
      serializer: IMiMigrationSerializer,
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
      serializer: IMiMigrationSerializer,
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

          match file.Name with
          | V0Name _
          | V1Name _ -> Some(file.Name, file.FullName |> File.ReadAllText)
          | _ -> None
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
      serializer: IMiMigrationSerializer,
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
            match file.Name with
            | V1Name _
            | V0Name _ -> Some file
            | _ -> None
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
type internal MiFileSystem
  (
    logger: ILogger,
    configurationSerializer: IMiConfigurationSerializer,
    migrationSerializer: IMiMigrationSerializer,
    projectRootUri: Uri,
    migrationsRootUri: Uri
  ) =

  let migrationsWorkingDir = Uri(projectRootUri, migrationsRootUri)

  interface IMiFileSystem with
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
        configurationSerializer,
        logger,
        projectRootUri,
        UMX.tag readFrom
      )

    member _.ReadConfigurationAsync(readFrom, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      PhysicalFileSystemImpl.readConfigurationAsync
        (configurationSerializer, logger, projectRootUri, UMX.tag readFrom)
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
        configurationSerializer,
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
        (configurationSerializer,
         logger,
         config,
         projectRootUri,
         UMX.tag writeTo)
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
        (migrationSerializer, logger, arg1, migrationsWorkingDir, UMX.tag arg2)
        token