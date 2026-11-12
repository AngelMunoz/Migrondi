namespace Migrondi.Mcp

/// A resolved per-call project binding: the config file that won, the
/// project root it anchors, and the fully layered config.
type ProjectBinding = {
  /// Absolute; the config file that won.
  ConfigPath: string
  /// Config's directory, or cwd.
  Root: string
  /// Fully layered (file -> env -> overrides).
  Config: Migrondi.Core.MigrondiConfig
}

/// Field overrides for the binding chain (MIGRONDI_* equivalents,
/// `--connection` & friends). Named record: anonymous records do not
/// unify across assemblies.
type ProjectOverrides = {
  connection: string option
  migrations: string option
  tableName: string option
  driver: string option
}

type ProjectResolveArgs = {
  Cwd: string
  ExplicitConfig: string option
  Overrides: ProjectOverrides
}

type ProjectResolveError =
  | ConfigNotFound of path: string
  | ConfigInvalid of path: string * reason: string

module ProjectBinding =

  /// --config wins and anchors Root at its directory (missing/unreadable/
  /// invalid file is a typed error). Otherwise cwd/migrondi.json with the
  /// CLI's fallback (missing -> Default), then MIGRONDI_* env vars, then
  /// explicit overrides. Re-resolved per call: config edits and env changes
  /// apply to the next call with no invalidation logic.
  val resolve:
    logger: Microsoft.Extensions.Logging.ILogger ->
    args: ProjectResolveArgs ->
      Result<ProjectBinding, ProjectResolveError>
