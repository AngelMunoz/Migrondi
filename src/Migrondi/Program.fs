open System
open System.IO

open System.CommandLine
open FSharp.SystemCommandLine

open Serilog
open Serilog.Formatting.Compact
open Serilog.Extensions.Logging

open Migrondi.Commands
open Migrondi.Env

[<EntryPoint>]
let main argv =

  let debug = argv |> Array.contains "[mi-debug]"

  let useJson = argv |> Array.contains "[mi-json]"

  // setup services
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

  let loggerFactory = new SerilogLoggerFactory(logger)

  let logger = loggerFactory.CreateLogger("Migrondi")

  let cwd = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}"
  let appEnv = AppEnv.BuildDefault(cwd, logger, useJson, argv)

  rootCommand argv {
    description
      "A dead simple SQL migrations runner, apply or rollback migrations at your ease"

    configure(fun pipeline ->
      // enable passing configuration flags before the actual commands
      pipeline.RootCommand.TreatUnmatchedTokensAsErrors <- false
    )

    noAction

    addCommands [
      Commands.Init appEnv
      Commands.New appEnv
      Commands.Up appEnv
      Commands.Down appEnv
      Commands.List appEnv
      Commands.Status appEnv
    ]

  }