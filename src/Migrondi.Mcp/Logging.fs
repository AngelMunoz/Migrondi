namespace Migrondi.Mcp

open System
open System.Reflection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Console

module McpLogging =

  /// Console logger factory for MCP hosts: every line goes to stderr,
  /// so stdout carries only MCP messages (spec 2026-07-28, stdio binding).
  let createLoggerFactory(minimumLevel: LogLevel) : ILoggerFactory =
    LoggerFactory.Create(fun builder ->
      builder
        .AddConsole(fun (options: ConsoleLoggerOptions) ->
          options.LogToStandardErrorThreshold <- LogLevel.Trace)
        .SetMinimumLevel(minimumLevel)
      |> ignore)

module McpVersion =

  /// The pack-time version from CHANGELOG.md (Ionide.KeepAChangelog.Tasks),
  /// falling back to the assembly version; build metadata after '+' is dropped.
  let get() : string =
    let assembly = Assembly.GetExecutingAssembly()

    match
      assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    with
    | null -> assembly.GetName().Version.ToString()
    | attr -> attr.InformationalVersion.Split('+').[0]
