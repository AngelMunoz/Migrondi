namespace Migrondi.Env

open System
open System.IO

open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.FileSystem
open Migrondi.Core.Migrondi
open Migrondi.Core.Serialization

open Microsoft.Extensions.Logging


type AppEnv
  (
    logger: ILogger,
    configSerializer: ConfigurationSerializer,
    migrationSerializer: MigrationSerializer,
    fs: FileSystemService,
    db: DatabaseService,
    migrondi: MigrondiService,
    jsonOutput: bool
  ) =

  member _.Logger: ILogger = logger

  member _.JsonOutput: bool = jsonOutput

  member _.Database: DatabaseService = db

  member _.FileSystem: FileSystemService = fs

  member _.Migrondi: MigrondiService = migrondi

  member _.ConfigurationSerializer: ConfigurationSerializer = configSerializer

  member _.MigrationSerializer: MigrationSerializer = migrationSerializer


  static member BuildDefault(cwd: string, logger: ILogger, jsonOutput) =
    let readLocalConfig
      (
        cwd: string,
        logger: ILogger,
        serializer: ConfigurationSerializer
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

    let configSerializer = SerializationFactory.GetConfigurationSerializer()
    let migrationSerializer = SerializationFactory.GetMigrationSerializer()

    let config = readLocalConfig(cwd, logger, configSerializer)


    let migrationsDir =
      if Path.EndsInDirectorySeparator(config.migrations) then
        config.migrations
      else
        $"{config.migrations}{Path.DirectorySeparatorChar}"



    let fs =
      FileSystemServiceFactory.GetInstance(
        Uri(cwd, UriKind.Absolute),
        Uri(migrationsDir, UriKind.Relative),
        logger,
        configSerializer,
        migrationSerializer
      )

    let db = DatabaseServiceFactory.GetInstance(logger, config)

    let migrondi = MigrondiServiceFactory.GetInstance(db, fs, logger, config)

    AppEnv(
      logger,
      configSerializer,
      migrationSerializer,
      fs,
      db,
      migrondi,
      jsonOutput
    )