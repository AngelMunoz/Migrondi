open System
open System.IO

open System.CommandLine
open FSharp.SystemCommandLine

open Microsoft.Extensions.Logging

open Serilog
open Serilog.Formatting.Compact
open Serilog.Extensions.Logging

open Migrondi.Commands
open Migrondi.Env
open Migrondi.Mcp

[<EntryPoint>]
let main argv =

  // MCP mode is detected before any logger exists: the mcp command's host
  // logger writes every console line to stderr so stdout carries only
  // MCP protocol messages.
  let isMcp = argv.Length > 0 && argv[0] = "mcp"

  let debug = argv |> Array.contains "[mi-debug]"

  let useJson = argv |> Array.contains "[mi-json]"

  // setup services
  let loggerFactory: ILoggerFactory =
    if isMcp then
      McpLogging.createLoggerFactory LogLevel.Information
    else
      let logger =
        let config = LoggerConfiguration().Enrich.FromLogContext()

        if debug then
          config.MinimumLevel.Debug() |> ignore
        else
          config.MinimumLevel.Information() |> ignore

        if useJson then
          config.WriteTo.Console(RenderedCompactJsonFormatter()) |> ignore
        else
          config.WriteTo.Console() |> ignore

        config.CreateLogger()

      new SerilogLoggerFactory(logger)

  let logger = loggerFactory.CreateLogger("Migrondi")

  let cwd = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}"
  let appEnv = AppEnv.BuildDefault(cwd, logger, useJson, argv)

  let cmd = ManualInvocation.rootCommand {
    description
      "A dead simple SQL migrations runner, apply or rollback migrations at your ease"

    noAction

    addCommands [
      Commands.Init appEnv
      Commands.New appEnv
      Commands.Up appEnv
      Commands.Down appEnv
      Commands.List appEnv
      Commands.Status appEnv
      Commands.Mcp loggerFactory cwd
    ]

  }

  cmd.TreatUnmatchedTokensAsErrors <- false

  cmd.Parse(argv).Invoke()
