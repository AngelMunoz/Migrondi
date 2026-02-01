(**
---
title: Database Services
category: Core
categoryindex: 3
index: 3
---
The database service requires a `MigrondiConfiguration` object, as we use information from the driver and connection string to establish a connection to the database.
*)

#r "../../src/Migrondi.Core/bin/Debug/net8.0/Migrondi.Core.dll"
#r "nuget: Microsoft.Extensions.Logging.Console, 9.0.0"
#r "nuget: FsToolkit.ErrorHandling"

open Migrondi.Core
open Microsoft.Extensions.Logging

let logger =
  // create a sample logger, you can provide your own
  LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
  |> fun x -> x.CreateLogger("Database")

let config = {
  MigrondiConfig.Default with
      connection =
        "Server=localhost;Database=master;User Id=sa;Password=yourStrong(!)Password;"
      driver = MigrondiDriver.Mssql
}

(**
The database service is used internally by the main Migrondi service. You typically don't need to use it directly.

When working with the database service through the main IMigrondi interface, you'll notice that it returns `MigrationRecord` objects which may be confusing with `Migration` objects.

The `MigrationRecord` object is a representation of the migration that is stored in the database, while the `Migration` object is a representation of the migration file. While they store similar information, their usage and purpose is different.
*)

(**
When you list migrations using the main service, you get records from the database that show which migrations have been applied.
*)

// Example of what MigrationRecord contains:
type MyMigrationRecord = {
  id: int64
  name: string
  timestamp: int64
}

(**
Keep in mind that the default behavior is to store the name of the migration including the timestamp e.g.
`1708216610033_initial-tables`
*)

(**
## Database Internals

Internally, when applying migrations, Migrondi works by:
1. Starting a database transaction
2. Executing the migration SQL content
3. If successful, inserting a MigrationRecord into the migrations table
4. Committing the transaction

If any step fails, the entire transaction is rolled back to ensure consistency.

The general mechanism is similar to this pattern using raw ADO.NET:
*)

open System.Data
open System.Data.Common

let toRun: Migration list = []
let connection: IDbConnection = null // obtained internally from config
let tableName = "__migrondi_migrations"

use transaction = connection.BeginTransaction()

for migration in toRun do
  let content = migration.upContent
  // run the content against the database
  use cmd = connection.CreateCommand()
  cmd.Transaction <- transaction
  cmd.CommandText <- content
  cmd.ExecuteNonQuery() |> ignore

  // insert migration record
  use insertCmd = connection.CreateCommand()

  insertCmd.CommandText <-
    $"""
    INSERT INTO "{tableName}" (name, timestamp)
    VALUES (@name, @timestamp)
    """

  insertCmd.Transaction <- transaction

  let nameParam = insertCmd.CreateParameter()
  nameParam.ParameterName <- "@name"
  nameParam.Value <- migration.name
  insertCmd.Parameters.Add(nameParam) |> ignore

  let timestampParam = insertCmd.CreateParameter()
  timestampParam.ParameterName <- "@timestamp"
  timestampParam.Value <- migration.timestamp
  insertCmd.Parameters.Add(timestampParam) |> ignore

  insertCmd.ExecuteNonQuery() |> ignore

transaction.Commit()

(**
The rollback process is similar, but instead of running `upContent` we run `downContent` and then remove the `MigrationRecord` from the database rather than inserting.

All of this is handled automatically by the IMigrondi service - you don't need to manage transactions directly unless you're implementing your own database handler.
*)