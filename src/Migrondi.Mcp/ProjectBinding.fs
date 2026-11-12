namespace Migrondi.Mcp

open System
open System.IO

open Microsoft.Extensions.Logging

open Migrondi.Core
open Migrondi.Core.Serialization

/// A resolved single-project binding. Constructed only by ProjectBinding.resolve.
type ProjectBinding = {
  /// Absolute path of the config file that won.
  ConfigPath: string
  /// Project root: the config's directory, or cwd.
  Root: string
  /// Fully layered config (file -> env -> overrides).
  Config: MigrondiConfig
}

/// Field overrides for the binding chain (MIGRONDI_* equivalents,
/// `--connection` & friends). A named record because anonymous records do
/// not unify across assemblies (tests build one).
type ProjectOverrides = {
  connection: string option
  migrations: string option
  tableName: string option
  driver: string option
}

/// Raw inputs for a binding resolution: the server's startup cwd, an
/// explicit config path when the caller targets a project, and the
/// host-provided field overrides.
type ProjectResolveArgs = {
  Cwd: string
  ExplicitConfig: string option
  Overrides: ProjectOverrides
}

/// Resolution order (roadmap Key Decisions):
/// `--config <path>` wins and anchors Root at its directory
/// (missing/invalid file is an error); otherwise `cwd/migrondi.json`
/// with the CLI's fallback behavior (missing -> Default + env + overrides).
type ProjectResolveError =
  | ConfigNotFound of path: string
  | ConfigInvalid of path: string * reason: string

module ProjectBinding =

  /// The CLI's env layering, reimplemented over a decoded `MigrondiConfig`
  /// (the same four MIGRONDI_* variables; an invalid MIGRONDI_DRIVER logs
  /// a warning and is ignored).
  let augmentFromEnvironment
    (logger: ILogger)
    (config: MigrondiConfig)
    : MigrondiConfig =
    let connection =
      Environment.GetEnvironmentVariable "MIGRONDI_CONNECTION_STRING"
      |> Option.ofObj

    let migrations =
      Environment.GetEnvironmentVariable "MIGRONDI_MIGRATIONS" |> Option.ofObj

    let tableName =
      Environment.GetEnvironmentVariable "MIGRONDI_TABLE_NAME" |> Option.ofObj

    let driver =
      Environment.GetEnvironmentVariable "MIGRONDI_DRIVER"
      |> Option.ofObj
      |> Option.bind(fun value ->
        try
          MigrondiDriver.FromString value |> Some
        with _ ->
          logger.LogWarning(
            "Invalid driver found at the MIGRONDI_DRIVER environment variable '{Message}'",
            value
          )

          None)

    {
      config with
          connection = connection |> Option.defaultValue config.connection
          migrations = migrations |> Option.defaultValue config.migrations
          tableName = tableName |> Option.defaultValue config.tableName
          driver = driver |> Option.defaultValue config.driver
    }

  /// The CLI's flag layering, over the typed `ProjectOverrides` instead of
  /// an anonymous record (an invalid driver logs a warning and falls back
  /// to the config's value).
  let applyOverrides
    (logger: ILogger)
    (overrides: ProjectOverrides)
    (config: MigrondiConfig)
    : MigrondiConfig = {
    config with
        connection =
          overrides.connection |> Option.defaultValue config.connection
        migrations =
          overrides.migrations |> Option.defaultValue config.migrations
        tableName = overrides.tableName |> Option.defaultValue config.tableName
        driver =
          overrides.driver
          |> Option.bind(fun value ->
            try
              MigrondiDriver.FromString value |> Some
            with _ ->
              logger.LogWarning(
                "Invalid driver at the cli flag '--driver': {Message}. Falling back to: {Driver}",
                value,
                config.driver.AsString
              )

              None)
          |> Option.defaultValue config.driver
  }

  /// `GetDirectoryName` returns null when the config sits at a drive root;
  /// the root itself is then the project root.
  let rootOf(fullPath: string) : string =
    match Path.GetDirectoryName fullPath with
    | null -> fullPath
    | directory -> directory

  /// Reads the configuration file at an explicit path, surfacing every
  /// error to the caller instead of falling back to defaults. The read is
  /// attempted directly — no existence probe — and the failure mode names
  /// itself.
  let readExplicitConfig
    (path: string, logger: ILogger)
    : Result<MigrondiConfig, ProjectResolveError> =
    let fullPath = Path.GetFullPath(path)

    logger.LogInformation(
      "Reading migrondi configuration from {ConfigPath}",
      fullPath
    )

    let fileContent =
      try
        File.ReadAllText fullPath |> Ok
      with
      | :? FileNotFoundException -> Error(ConfigNotFound fullPath)
      | :? DirectoryNotFoundException -> Error(ConfigNotFound fullPath)
      | ex -> Error(ConfigInvalid(fullPath, ex.Message))

    fileContent
    |> Result.bind(fun content ->
      try
        Ok(MiSerializer.DecodeConfig content)
      with DeserializationFailed(_, reason) ->
        Error(ConfigInvalid(fullPath, reason)))

  /// The CLI's cwd fallback: a missing `cwd/migrondi.json` warns and uses
  /// the defaults; an invalid one logs a critical line and does the same.
  let readLocalConfig(cwd: string, logger: ILogger) : MigrondiConfig option =
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
        MiSerializer.DecodeConfig content |> Some
      with DeserializationFailed(_, reason) ->
        logger.LogCritical(
          "Invalid local configuration file '{Message}', aborting.",
          reason
        )

        None)

  let resolve
    (logger: ILogger)
    (args: ProjectResolveArgs)
    : Result<ProjectBinding, ProjectResolveError> =

    let layer config =
      config
      |> augmentFromEnvironment logger
      |> applyOverrides logger args.Overrides

    match args.ExplicitConfig with
    | Some path ->
      let fullPath = Path.GetFullPath(path)

      readExplicitConfig(fullPath, logger)
      |> Result.map(fun decoded -> {
        ConfigPath = fullPath
        Root = rootOf fullPath
        Config = layer decoded
      })
    | None ->
      let config =
        readLocalConfig(args.Cwd, logger)
        |> Option.defaultWith(fun _ -> MigrondiConfig.Default)
        |> layer

      Ok {
        ConfigPath = Path.Combine(args.Cwd, "migrondi.json")
        Root = args.Cwd
        Config = config
      }
