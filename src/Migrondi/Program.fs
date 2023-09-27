open System
open System.CommandLine.Builder
open System.IO
open FSharp.SystemCommandLine

open Serilog

open Migrondi.Core

open Migrondi.Commands
open Migrondi.Env
open Migrondi.Middleware

[<EntryPoint>]
let main argv =

  // setup services
  let logger =
    LoggerConfiguration()
      .MinimumLevel.Information()
      .WriteTo.Console()
      .CreateLogger()

  let cwd = $"{Environment.CurrentDirectory}{Path.DirectorySeparatorChar}"
  let appEnv = AppEnv.BuildDefault(cwd, logger)


  rootCommand argv {
    description
      "A dead simple SQL migrations runner, apply or rollback migrations at your ease"

    usePipeline(fun pipeline ->
      // run the setup database for
      pipeline.AddMiddleware(Middleware.SetupDatabase appEnv) |> ignore
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