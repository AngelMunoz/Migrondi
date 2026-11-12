namespace Migrondi.Mcp

open System
open System.IO

open Migrondi.Core
open Migrondi.Core.Serialization

/// Single source of truth for init semantics, shared by the CLI's init
/// handler and the `init_project` tool: what to do for a directory, how a
/// partial config file is recovered, and how a project's files get written.
module ProjectInit =

  /// What the caller should do for a directory, given its state and force.
  type InitDecision =
    | CreateFresh // directory empty (or nonexistent): write defaults
    | AdoptExisting // non-empty + force: recover fields, rewrite, keep files
    | RefuseNonEmpty // non-empty, no force

  let decide (isEmpty: bool) (force: bool) : InitDecision =
    match isEmpty, force with
    | true, _ -> CreateFresh
    | false, true -> AdoptExisting
    | false, false -> RefuseNonEmpty

  let isDirectoryEmpty(path: DirectoryInfo) : bool =
    try
      let noDirs =
        Directory.EnumerateDirectories(
          path.FullName,
          "*",
          SearchOption.AllDirectories
        )
        |> Seq.isEmpty

      let noFiles =
        Directory.EnumerateFiles(
          path.FullName,
          "*",
          SearchOption.AllDirectories
        )
        |> Seq.isEmpty

      noDirs && noFiles
    with :? DirectoryNotFoundException ->
      path.Create()
      true

  /// Recovers known fields from arbitrary JSON (`Map<string,string>` view),
  /// filling gaps with `MigrondiConfig.Default`.
  let recoverConfig(values: Map<string, string>) : MigrondiConfig =
    let defaults = MigrondiConfig.Default

    let driver =
      values
      |> Map.tryFind "driver"
      |> Option.bind(fun value ->
        try
          MigrondiDriver.FromString value |> Some
        with _ ->
          None)
      |> Option.defaultValue defaults.driver

    {
      connection =
        values
        |> Map.tryFind "connection"
        |> Option.defaultValue defaults.connection
      migrations =
        values
        |> Map.tryFind "migrations"
        |> Option.defaultValue defaults.migrations
      tableName =
        values
        |> Map.tryFind "tableName"
        |> Option.defaultValue defaults.tableName
      driver = driver
    }

  /// Applies optional caller-provided fields on top of a base config. An
  /// invalid driver value falls back to the base config's driver; callers
  /// that must report invalid drivers as errors validate before calling.
  let applyOverrides
    (config: MigrondiConfig)
    (overrides: ProjectOverrides)
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
              None)
          |> Option.defaultValue config.driver
  }

  let writeProject
    (directory: DirectoryInfo)
    (config: MigrondiConfig)
    : Result<unit, string> =
    try
      directory.Create()

      let configPath = Path.Combine(directory.FullName, "migrondi.json")

      File.WriteAllText(configPath, MiSerializer.Encode config)

      if
        Path.IsPathRooted config.migrations
        || Path.IsPathFullyQualified config.migrations
      then
        DirectoryInfo(config.migrations).Create()
      else
        directory.CreateSubdirectory config.migrations |> ignore

      Ok()
    with ex ->
      Error ex.Message
