#r "nuget: Microsoft.Extensions.Logging"
#r "nuget: Microsoft.Extensions.Logging.Console"
#r "nuget: Migrondi.Core, 1.0.0-beta-002"

open System
open System.IO
open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.FileSystem
open Migrondi.Core.Migrondi
open Migrondi.Core.Serialization

open Microsoft.Extensions.Logging

module Config =
  let getLogger loggerName =
    let loggerFactory =
      LoggerFactory.Create(fun builder -> builder.SetMinimumLevel(LogLevel.Debug).AddSimpleConsole() |> ignore)

    loggerFactory.CreateLogger loggerName

  let ensureWellFormed (migrationsDir: string) =
    // when you're using URIs "./path" is not the same as "./path/"
    // the first one is a file and the second one is a directory
    // so ensure you always end your paths with a directory separator

    if Path.EndsInDirectorySeparator(migrationsDir) then
      migrationsDir
    else
      $"{migrationsDir}{Path.DirectorySeparatorChar}"

// Get a serializer instance this works for both configuration and SQL files
let serializer = SerializerServiceFactory.GetInstance()

let config = MigrondiConfig.Default
let logger = Config.getLogger("sample-app")

// once we have a serializer, a logger, and a configuration object we can start working with the rest of the library

let fileSystemService =
  let migrationsDir = Config.ensureWellFormed config.migrations

  FileSystemServiceFactory.GetInstance(
    serializer,
    Uri(__SOURCE_DIRECTORY__ + $"{Path.DirectorySeparatorChar}", UriKind.Absolute),
    Uri(migrationsDir, UriKind.Relative)
  )

let databaseService = DatabaseServiceFactory.GetInstance(logger, config)

databaseService.SetupDatabase()

let migrondi =
  MigrondiServiceFactory.GetInstance(
    databaseService,
    fileSystemService,
    logger,
    config
  )

// Let's create a new Migration

let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
let name = $"add-test-table_{timestamp}.sql"

fileSystemService.WriteMigration(
  {
    name = name
    timestamp = timestamp
    upContent = "create table if not exists test (id int not null primary key);"
    downContent = "drop table if exists test;"
  },
  name
)

let applied = migrondi.DryRunUp()

printfn "List of the migrations that would have been ran: %A" applied