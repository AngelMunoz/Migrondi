namespace Migrondi.Env

open System
open System.IO

open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization

open Microsoft.Extensions.Logging


type internal AppEnv
  (
    logger: ILogger,
    configSerializer: IMiConfigurationSerializer,
    migrationSerializer: IMiMigrationSerializer,
    fs: IMiFileSystem,
    db: IMiDatabaseHandler,
    migrondi: IMigrondi,
    jsonOutput: bool
  ) =

  member _.Logger: ILogger = logger

  member _.JsonOutput: bool = jsonOutput

  member _.Database: IMiDatabaseHandler = db

  member _.FileSystem: IMiFileSystem = fs

  member _.Migrondi: IMigrondi = migrondi

  member _.ConfigurationSerializer: IMiConfigurationSerializer =
    configSerializer

  member _.MigrationSerializer: IMiMigrationSerializer = migrationSerializer


  static member BuildDefault(cwd: string, logger: ILogger, jsonOutput) =
    let readLocalConfig
      (
        cwd: string,
        logger: ILogger,
        serializer: IMiConfigurationSerializer
      ) =
      let fileContent =
        try
          File.ReadAllText(Path.Combine(cwd, "migrondi.json")) |> Some
        with _ ->
          None

      match fileContent with
      | Some(content) ->
        try
          serializer.Decode(content)
        with DeserializationFailed(_, reason) ->
          logger.LogCritical(
            "Invalid local configuration file '{Message}', aborting.",
            reason
          )

          exit(1)
      | None -> MigrondiConfig.Default

    let serializer = MigrondiSerializer()

    let config = readLocalConfig(cwd, logger, serializer)


    let migrationsDir =
      if Path.EndsInDirectorySeparator(config.migrations) then
        config.migrations
      else
        $"{config.migrations}{Path.DirectorySeparatorChar}"



    let fs =
      MiFileSystem(
        logger,
        serializer,
        serializer,
        Uri(cwd, UriKind.Absolute),
        Uri(migrationsDir, UriKind.Relative)
      )

    let db = MiDatabaseHandler(logger, config)

    let migrondi = Migrondi(config, db, fs, logger)

    AppEnv(logger, serializer, serializer, fs, db, migrondi, jsonOutput)