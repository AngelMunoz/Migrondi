---
title: Use as a library (F#/VB/C#)
---

From v1 and onwards Migrondi was built to be used as a library. This means that you can use Migrondi to run your own migrations from F# or C# code. and perhaps even extend Migrondi functionality. with your own.

## Usage

First, get it from NuGet

> `dotnet add package Migrondi.Core`

After that most of the core is under it's own namespage e.g.

- `Migrondi.Core.Database`
- `Migrondi.Core.Migrondi`

If you plan to use a local file system and not many customization then you can use the default implementations that come with the library.

### F# Examples

```fsharp
open System
open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.FileSystem
open Migrondi.Core.Migrondi
open Migrondi.Core.Serialization

open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Console

module Config =
    // Sample function to get a MigrondiConfig instance from disk
    let getConfig (serializer: SerializerService) =
        let content = File.ReadAllText "./migrondi.json"
        serializer
          .ConfigurationSerializer
          .Decode(content)

    let getLogger loggerName =
      loggerFactory =
        LoggerFactory.Create(builder ->
            builder.AddSimpleConsole() |> ignore
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

// Get a serializer instance this works for both configuration and SQL files
let serializer = SerializerFactory.GetInstance()

let config = Config.getConfig(serializer)
let logger = Config.getLogger("sample-app")

// once we have a serializer, a logger, and a configuration object we can start working with the rest of the library

let fileSystemService =
  let migrationsDir = Config.ensureWellFormed config.MigrationsDir
  FileSystemServiceFactory.GetInstance(
    serializer,
    Uri(Environment.CurrentDirectory, UriKind.Absolute),
    Uri(migrationsDir, UriKind.Relative)
  )

let databaseService = DatabaseServiceFactory.GetInstance(logger, config)

let migrondi =
  MigrondiServiceFactory.GetInstance(
    databaseService,
    fileSystemService,
    logger,
    config
  )


// Let's create a new Migration

let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
let name = $"{name}_{timestamp}.sql"

fileSystemService.WriteMigration(
  {
    name = name
    timestamp = timestamp
    upContent =
      "create table if not exists test (id int not null primary key);"
    downContent =
      "drop table if exists test;"
  },
  name
)

let applied = migrondi.RunUp()

printfn "All of the following migrations were applied: %A" applied

```
