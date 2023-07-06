open System.CommandLine
open FSharp.SystemCommandLine

open Serilog

open Migrondi.Core

open Migrondi.Commands
open Migrondi.Env

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