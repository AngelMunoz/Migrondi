namespace Migrondi.Env

open System
open System.IO

open Microsoft.Extensions.Logging

open System.CommandLine
open System.CommandLine.Parsing
open FSharp.SystemCommandLine

open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization

module internal Configuration =


  let configParser(values: string[]) =

    let connectionOption =
      Input.option<string> "--connection"
      |> Input.aliases [ "-c" ]
      |> Input.desc "The connection string to the database."

    let migrationsOption =
      Input.option<string> "--migrations"
      |> Input.aliases [ "-m" ]
      |> Input.desc "The path to the migrations directory."

    let tableNameOption =
      Input.option<string> "--table-name"
      |> Input.aliases [ "-t" ]
      |> Input.desc "The name of the table that will store the migrations."


    let driverOption =
      let op = Option<string> "--driver"

      op.AcceptOnlyFromAmong("mssql", "sqlite", "postgresql", "mysql")
      |> Input.ofOption
      |> Input.aliases [ "-d" ]
      |> Input.desc "The driver that will be used to connect to the database."


    let mutable cO = None
    let mutable mO = None
    let mutable tNO = None
    let mutable dO = None


    rootCommand values {
      description "Migrondi Configuration Parser"

      configure(fun cfg ->
        cfg.RootCommand.TreatUnmatchedTokensAsErrors <- false)

      inputs(connectionOption, migrationsOption, tableNameOption, driverOption)

      setAction(fun (c: string, m: string, t: string, d: string) ->
        let binder v =
          match v with
          | null
          | "" -> None
          | v -> Some v

        cO <- binder c
        mO <- binder m
        tNO <- binder t
        dO <- binder d)
    }
    |> ignore

    {|
      connection = cO
      migrations = mO
      tableName = tNO
      driver = dO
    |}

  let augmentFromEnvironment
    (log: ILogger)
    (config: MigrondiConfig option)
    : MigrondiConfig =
    let connection =
      Environment.GetEnvironmentVariable("MIGRONDI_CONNECTION_STRING")
      |> Option.ofObj

    let migrations =
      Environment.GetEnvironmentVariable("MIGRONDI_MIGRATIONS") |> Option.ofObj

    let tableName =
      Environment.GetEnvironmentVariable("MIGRONDI_TABLE_NAME") |> Option.ofObj

    let driver =
      Environment.GetEnvironmentVariable("MIGRONDI_DRIVER")
      |> Option.ofObj
      |> Option.bind(fun value ->
        try
          MigrondiDriver.FromString value |> Some
        with _ ->
          log.LogWarning(
            "Invalid driver found at the MIGRONDI_DRIVER environment variable '{Message}'",
            value
          )

          None)

    match config with
    | Some cfg -> {
        cfg with
            connection = connection |> Option.defaultValue cfg.connection
            migrations = migrations |> Option.defaultValue cfg.migrations
            tableName = tableName |> Option.defaultValue cfg.tableName
            driver = driver |> Option.defaultValue cfg.driver
      }
    | None ->
      let defaultConfig = MigrondiConfig.Default

      {
        defaultConfig with
            connection =
              connection |> Option.defaultValue defaultConfig.connection
            migrations =
              migrations |> Option.defaultValue defaultConfig.migrations
            tableName = tableName |> Option.defaultValue defaultConfig.tableName
            driver = driver |> Option.defaultValue defaultConfig.driver
      }


  let augmentFromCli
    (log: ILogger)
    (args:
      {|
        connection: string option
        migrations: string option
        tableName: string option
        driver: string option
      |})
    : MigrondiConfig -> MigrondiConfig =
    fun config -> {
      config with
          connection = args.connection |> Option.defaultValue config.connection
          migrations = args.migrations |> Option.defaultValue config.migrations
          tableName = args.tableName |> Option.defaultValue config.tableName
          driver =
            args.driver
            |> Option.bind(fun value ->
              try
                MigrondiDriver.FromString value |> Some
              with _ ->
                log.LogWarning(
                  "Invalid driver at the cli flag '--driver': {Message}. Falling back to: {Driver}",
                  value,
                  config.driver.AsString
                )

                None)
            |> Option.defaultValue config.driver
    }

  let readLocalConfig
    (cwd: string, logger: ILogger, serializer: IMiConfigurationSerializer)
    =
    let fileContent =
      try
        File.ReadAllText(Path.Combine(cwd, "migrondi.json")) |> Some
      with _ ->
        logger.LogWarning(
          "Configuration File Not Found, falling back to the default configuration."
        )

        None

    fileContent
    |> Option.bind(fun content ->
      try
        serializer.Decode(content) |> Some
      with DeserializationFailed(_, reason) ->
        logger.LogCritical(
          "Invalid local configuration file '{Message}', aborting.",
          reason
        )

        None)



type internal AppEnv
  (
    logger: ILogger,
    configSerializer: IMiConfigurationSerializer,
    migrationSerializer: IMiMigrationSerializer,
    fs: IMiFileSystem,
    db: IMiDatabaseHandler,
    migrondi: IMigrondi,
    jsonOutput: bool,
    configuration: MigrondiConfig
  ) =

  member _.Logger: ILogger = logger

  member _.JsonOutput: bool = jsonOutput

  member _.Database: IMiDatabaseHandler = db

  member _.FileSystem: IMiFileSystem = fs

  member _.Migrondi: IMigrondi = migrondi

  member _.ConfigurationSerializer: IMiConfigurationSerializer =
    configSerializer

  member _.MigrationSerializer: IMiMigrationSerializer = migrationSerializer
  member _.Configuration: MigrondiConfig = configuration


  static member BuildDefault(cwd: string, logger: ILogger, jsonOutput, argv) =
    let serializer = MigrondiSerializer()

    let config =
      Configuration.readLocalConfig(cwd, logger, serializer)
      |> Configuration.augmentFromEnvironment logger
      |> (Configuration.configParser argv |> Configuration.augmentFromCli logger)


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

    let migrondi = Migrondi.MigrondiFactory(config, cwd, logger = logger)

    AppEnv(logger, serializer, serializer, fs, db, migrondi, jsonOutput, config)
