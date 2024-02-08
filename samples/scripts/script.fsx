#r "nuget: Microsoft.Extensions.Logging"
#r "nuget: Microsoft.Extensions.Logging.Console"
#r "nuget: Migrondi.Core, 1.0.0-beta-007"

open System
open System.IO
open Migrondi.Core

open Microsoft.Extensions.Logging

module Config =
  let getLogger loggerName =
    let loggerFactory =
      LoggerFactory.Create(fun builder ->
        builder.SetMinimumLevel(LogLevel.Debug).AddSimpleConsole() |> ignore
      )

    loggerFactory.CreateLogger loggerName

  let ensureWellFormed (migrationsDir: string) =
    // when you're using URIs "./path" is not the same as "./path/"
    // the first one is a file and the second one is a directory
    // so ensure you always end your paths with a directory separator

    if Path.EndsInDirectorySeparator(migrationsDir) then
      migrationsDir
    else
      $"{migrationsDir}{Path.DirectorySeparatorChar}"


let config = MigrondiConfig.Default
let logger = Config.getLogger("sample-app")

let factory = Migrondi.MigrondiFactory(logger)
let migrationsDir = Config.ensureWellFormed config.migrations

let migrondi =
  factory.Invoke(
    config,
    Uri(
      __SOURCE_DIRECTORY__ + $"{Path.DirectorySeparatorChar}",
      UriKind.Absolute
    ),
    Uri(migrationsDir, UriKind.Relative)
  )

// Let's create a new Migration
migrondi.RunNew(
  "add-test-table",
  "create table if not exists test (id int not null primary key);",
  "drop table if exists test;"
)


let applied = migrondi.DryRunUp()

logger.LogInformation(
  $"List of the migrations that would have been ran: %A{applied}"
)