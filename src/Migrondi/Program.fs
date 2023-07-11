open System.CommandLine
open System.CommandLine.Invocation
open System.CommandLine.Builder
open FSharp.SystemCommandLine

open Serilog

open Migrondi.Core

open Migrondi.Commands
open Migrondi.Env
open Migrondi.Middleware

[<EntryPoint>]
let main argv =

  let logger =
    LoggerConfiguration()
      .MinimumLevel.Information()
      .WriteTo.Console()
      .CreateLogger()

  let appEnv = AppEnv(logger, Migrondi())


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