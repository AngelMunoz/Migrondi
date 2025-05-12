(**
---
title: Database Services
category: Core
categoryindex: 3
index: 3
---
The database service requires a `MigrondiConfiguration` object, as we use the information from the driver and connection string to stablish a connection to the database.
*)

#r "nuget: Migrondi.Core, 1.0.0-beta-012"

open Migrondi.Core
open Migrondi.Core.Database
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

// create a new instance of the database handler
let db: IMiDatabaseHandler = MiDatabaseHandler(logger, config)

(**
Before working with the database service, you need call the `SetupDatabase()` call to ensure that the migrations table exists.

Currently, the queries to execute the setup are not exposed, so if you create your own `IMiDatabaseHandler` implementation, you need to be sure you're creating the correct tracking mechanism for the migrations. In our default case it is just tables in the database but keep it in mind.
*)

db.SetupDatabase()

(**
Once the database is setup, you can start working with migrations.

You will notice that the `IMiDatabaseHandler` interface returns `MigrationRecord` objects which may be confusing with `Migration` objects.

The `MigrationRecord` object is a representation of the migration that is stored in the database, while the `Migration` object is a representation of the migration file. While they store similar information, their usage and purpose is different.

*)

// Find what was the last migration applied
let migrations = db.FindLastApplied()

// this method returns an option in case there's no migration applied.


(**
In order to obtain the migrations in the database you can call the `ListMigrations()` method. It has no parameters and returns a list of `MigrationRecord` objects.
*)
let existingMigrations = db.ListMigrations()

(**
For cases where you want to check if a particular migration has been applied, you can use the `FindMigration(name)` method. It returns an option with the `MigrationRecord` object if it exists.

Keep in mind that the default behavior is to store the name of the migration including the timesamp e.g.
*)

let foundMigration = db.FindMigration("initial-tables_1708216610033")

(**
> ***NOTE***: Keep in mind that if you are using a custom implementation of the `IMiDatabaseHandler` interface, you need to ensure that the `MigrationRecord` object stores the name properly as well.

Applying and Rolling back migrations is fairly straight forward as well. The general mechanism is to simply iterate over the migration objects, run their SQL content and then store a `MigrationRecord` object in the database.

A brief example of how we do that internally is as follows:

*)

open System.Data
open RepoDb

let toRun: Migration list = []
let connection: IDbConnection = null // get it from somewhere

for migration in toRun do
  let content = migration.upContent
  // run the content against the database
  connection.ExecuteNonQuery($"{content};;") |> ignore

  connection.Insert(
    tableName = config.tableName,
    entity = {|
      name = migration.name
      timestamp = migration.timestamp
    |},
    fields = Field.From("name", "timestamp")
  )
  |> ignore

(**
The rollback process is similar, but instead of running the `upContent` we run the `downContent` and then remove the `MigrationRecord` from the database rather than inserting.

In your case you would need to ensure that the `IMiDatabaseHandler` implementation you are using save the `MigrationRecord` objects properly as well as applying the the migration content to your data source.
*)