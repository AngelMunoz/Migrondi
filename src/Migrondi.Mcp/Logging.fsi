namespace Migrondi.Mcp

module McpLogging =

  /// Console logger factory for MCP hosts: every line goes to stderr,
  /// so stdout carries only MCP messages (spec 2026-07-28, stdio binding).
  val createLoggerFactory:
    minimumLevel: Microsoft.Extensions.Logging.LogLevel ->
      Microsoft.Extensions.Logging.ILoggerFactory

module McpVersion =

  /// The pack-time version from CHANGELOG.md (Ionide.KeepAChangelog.Tasks),
  /// falling back to the assembly version; build metadata after '+' is dropped.
  val get: unit -> string
