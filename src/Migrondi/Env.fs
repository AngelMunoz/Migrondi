namespace Migrondi.Env

open System
open System.IO
open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.FileSystem
open Migrondi.Core.Migrondi
open Migrondi.Core.Serialization
open Serilog


type AppEnv
  (
    logger: ILogger,
    fs: FileSystemService,
    db: DatabaseService,
    migrondi: MigrondiService
  ) =

  member _.Logger: ILogger = logger

  member _.Database: DatabaseService = db

  member _.FileSystem: FileSystemService = fs

  member _.Migrondi: MigrondiService = migrondi


  static member BuildDefault(cwd: string, logger: ILogger) =
    let readLocalConfig
      (
        cwd: string,
        logger: ILogger,
        serializer: SerializerService
      ) =
      let fileContent =
        try
          File.ReadAllText(Path.Combine(cwd, "migrondi.json")) |> Some
        with _ ->
          None

      match fileContent with
      | Some(content) ->
        try
          serializer.ConfigurationSerializer.Decode(content)
        with DeserializationFailed(_, reason) ->
          logger.Fatal(
            "Invalid local configuration file '{Message}', aborting.",
            reason
          )

          exit(1)
      | None -> MigrondiConfig.Default


    let serializer = SerializerImpl.BuildDefaultEnv()

    let config = readLocalConfig(cwd, logger, serializer)

    let migrationsDir =
      if Path.EndsInDirectorySeparator(config.migrations) then
        config.migrations
      else
        $"{config.migrations}{Path.DirectorySeparatorChar}"

    let fs =
      FileSystemImpl.BuildDefaultEnv(
        serializer,
        Uri(cwd, UriKind.Absolute),
        Uri(migrationsDir, UriKind.Relative)
      )

    let db = DatabaseImpl.Build(config)
    let migrondi = MigrondiServiceImpl.BuildDefaultEnv(db, fs, config)

    AppEnv(logger, fs, db, migrondi)