(**
---
title: F# Scripts
category: Examples
categoryindex: 1
---
Migrondi can be used in F# scripts, which is a great way to get the hang of the library and easy to prototype.

The following example shows a couple of things

- Get the dependencies for the default services
- Setup the Database
- Create a new migration
- Do a dry run of the migrations

We'll start with a little prelude of the code that is needed to get the dependencies for the services.
and opening the required namespaces.
*)

#r "nuget: Microsoft.Extensions.Logging"
#r "nuget: Microsoft.Extensions.Logging.Console"
#r "nuget: Migrondi.Core, 1.0.0-beta-003"

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

(**
We need to get a serializer, a logger, and a configuration object to create the services.
mostly because this is designed to play well with DI containers.


 *)
let config = MigrondiConfig.Default

let logger = Config.getLogger("sample-app")

let serializer = SerializerServiceFactory.GetInstance()

let fileSystemService =
  let migrationsDir = Config.ensureWellFormed config.migrations

  FileSystemServiceFactory.GetInstance(
    serializer,
    logger,
    Uri(
      __SOURCE_DIRECTORY__ + $"{Path.DirectorySeparatorChar}",
      UriKind.Absolute
    ),
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

(**
For any database related work you _MUST_ call `SetupDatabase` before doing anything else.
Otherwise the driver won't be initialized and the tables won't be created.
*)


databaseService.SetupDatabase()

(**
We can start with a simple migration.

> The name schema for the migrations is basically a string separated by an underscode and a timestamp followed by the .sql extension

> ```fsharp
> let MigrationNameSchema = "(.+)_([0-9]+).(sql|SQL)"
> ```
*)

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


(**
Once the services are in place and we have a migration in the local file system then we can
Attempt a dry run of the migrations.

Dry runs only return the scripts that would be executed in the order they would be executed.
so you can `for migration in migrations do ...` and review that everything comes in order.
*)

let applied = migrondi.DryRunUp()

logger.LogInformation(
  $"List of the migrations that would have been ran: %A{applied}"
)

(**
That should show you a list of the migrations that would have been ran.

```text
dbug: sample-app[0]
      Writing migration to c:\path\to\scripts\migrations\add-test-table_1696144424109.sql with directory: c:\path\to\scripts\migrations\ and name: add-test-table_1696144424109.sql
dbug: sample-app[0]
      Listing migrations from c:\path\to\scripts\migrations with directory: ./migrations
info: sample-app[0]
      List of the migrations that would have been ran: [{ name = "add-test-table_1696144424109"
   timestamp = 1696144424109L
   upContent = "create table if not exists test (id int not null primary key);"
   downContent = "drop table if exists test;" }]
```

Keep in mind that the migrations are not actually ran, so the database is not updated.
From there on you can encode your logic specifically to know when to apply or revert migrations.
*)