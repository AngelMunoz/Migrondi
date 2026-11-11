namespace Migrondi.Env

open System
open System.IO

open Microsoft.Extensions.Logging

open Migrondi.Core
open Migrondi.Core.Serialization

/// A resolved single-project binding. Constructed only by McpBinding.resolve.
type McpBinding = {
  /// Absolute path of the config file that won.
  ConfigPath: string
  /// Project root: the config's directory, or cwd.
  Root: string
  /// Fully layered config (file -> env -> cli).
  Config: MigrondiConfig
}

/// CLI-level config overrides for the MCP binding (`--connection`,
/// `--migrations`, `--table-name`, `--driver`). A named record because
/// anonymous records do not unify across assemblies (tests build one).
type McpOverrides = {
  connection: string option
  migrations: string option
  tableName: string option
  driver: string option
}

/// Raw inputs for the MCP binding resolution, built once by the command's
/// argument mapper.
type McpResolveArgs = {
  Cwd: string
  ExplicitConfig: string option
  Overrides: McpOverrides
}

/// Resolution order (roadmap Key Decisions):
/// `--config <path>` wins and anchors Root at its directory
/// (missing/invalid file is an error); otherwise `cwd/migrondi.json`
/// with the CLI's fallback behavior (missing -> Default + env + cli overrides).
type McpResolveError =
  | ConfigNotFound of path: string
  | ConfigInvalid of path: string * reason: string

module internal McpBinding =

  let resolve
    (logger: ILogger)
    (args: McpResolveArgs)
    : Result<McpBinding, McpResolveError> =

    let serializer = MigrondiSerializer()

    // `augmentFromCli` takes an anonymous record; anonymous records do not
    // unify across assemblies, so the public McpOverrides record is rebuilt
    // into that shape here.
    let overrides = {|
      connection = args.Overrides.connection
      migrations = args.Overrides.migrations
      tableName = args.Overrides.tableName
      driver = args.Overrides.driver
    |}

    match args.ExplicitConfig with
    | Some path ->
      let fullPath = Path.GetFullPath(path)

      // No existence probe: the read itself reports a missing file as
      // `NotFound`, an unreadable one as `Unreadable`, and bad JSON as
      // `Invalid`.
      match Configuration.readConfigAt(fullPath, logger, serializer) with
      | Error(Configuration.ConfigReadError.NotFound filePath) ->
        Error(ConfigNotFound filePath)
      | Error(Configuration.ConfigReadError.Unreadable(filePath, reason))
      | Error(Configuration.ConfigReadError.Invalid(filePath, reason)) ->
        Error(ConfigInvalid(filePath, reason))
      | Ok decoded ->
        let config =
          Some decoded
          |> Configuration.augmentFromEnvironment logger
          |> Configuration.augmentFromCli logger overrides

        // `GetDirectoryName` returns null when the config sits at a
        // drive root; the root itself is then the project root.
        let root =
          match Path.GetDirectoryName fullPath with
          | null -> fullPath
          | directory -> directory

        Ok {
          ConfigPath = fullPath
          Root = root
          Config = config
        }
    | None ->
      let config =
        Configuration.readLocalConfig(args.Cwd, logger, serializer)
        |> Configuration.augmentFromEnvironment logger
        |> Configuration.augmentFromCli logger overrides

      Ok {
        ConfigPath = Path.Combine(args.Cwd, "migrondi.json")
        Root = args.Cwd
        Config = config
      }
