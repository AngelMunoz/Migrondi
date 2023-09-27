open System
open System.IO

open System.CommandLine.Builder
open FSharp.SystemCommandLine

open Serilog
open Serilog.Formatting.Compact

open Migrondi.Core
open Migrondi.Commands
open Migrondi.Env
open Migrondi.Middleware

[<EntryPoint>]
let main argv =

  let debug = argv |> Array.contains "[mi-debug]"

  let useJson = argv |> Array.contains "[mi-json]"

  // setup services
  let logger =
    let config = LoggerConfiguration()

    if debug then
      config.MinimumLevel.Debug() |> ignore
    else
      config.MinimumLevel.Information() |> ignore

    if useJson then
      config.WriteTo.Console(new RenderedCompactJsonFormatter()) |> ignore
    else
      config.WriteTo.Console() |> ignore

    config.CreateLogger()


  let cwd = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}"
  let appEnv = AppEnv.BuildDefault(cwd, logger, useJson)


  rootCommand argv {
    description
      "A dead simple SQL migrations runner, apply or rollback migrations at your ease"

    usePipeline(fun pipeline ->
      pipeline
        .EnableDirectives(true)
        // run the setup database for select commands
        .AddMiddleware(Middleware.SetupDatabase appEnv)
      |> ignore
    )

    setHandler id

    addCommands [
      Commands.Init appEnv
      Commands.New appEnv
      Commands.Up appEnv
      Commands.Down appEnv
      Commands.List appEnv
      Commands.Status appEnv
    ]

  }