namespace Migrondi.Tests.Database

open System

open Microsoft.VisualStudio.TestTools.UnitTesting

open RepoDb

open FsToolkit.ErrorHandling

open Migrondi.Core
open Migrondi.Core.Database


module DatabaseData =
  open System.Data

  let getConfig (dbName: Guid) = {
    connection = $"Data Source=./{dbName.ToString()}.db"
    driver = MigrondiDriver.Sqlite
    migrations = "./migrations"
    tableName = "migration_records_database_test"
  }

  let createTableQuery tableName =
    $"""CREATE TABLE IF NOT EXISTS %s{tableName}(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name VARCHAR(255) NOT NULL,
  timestamp BIGINT NOT NULL
);"""

  let insertMigrationRecords
    (records: int)
    (tableName: string)
    (connection: IDbConnection)
    =
    let records = [
      for i in 0..records do
        {
          id = int64(i + 1)
          name = $"test_{i + 1}"
          timestamp =
            DateTimeOffset.UtcNow
              .AddMinutes(float(i + 1))
              .ToUnixTimeMilliseconds()
        }
    ]

    let connection = connection.EnsureOpen()
    use transaction = connection.BeginTransaction()

    let result =
      connection.InsertAll<MigrationRecord>(
        tableName = tableName,
        entities = records,
        transaction = transaction
      )

    transaction.Commit()
    result



[<TestClass>]
type DatabaseTests() =
  let dbName = Guid.NewGuid()
  let config = DatabaseData.getConfig dbName
  let databaseEnv = DatabaseImpl.Build(DatabaseData.getConfig dbName)

  [<TestInitialize>]
  member _.TestInitialize() =
    printfn "Initializing Database Tests"
    printfn $"Connecting To: {config.connection}"
    printfn $"Using Driver: {config.driver}"
    printfn $"Using Table Name: {config.tableName}"

  [<TestCleanup>]
  member _.TestCleanup() =
    System.IO.File.Delete($"./{dbName.ToString()}.db")

  [<TestMethod>]
  member _.``Database Should be Setup Correctly``() =
    let operation = result {
      do! databaseEnv.SetupDatabase()

      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      return!
        connection.ExecuteQuery<string>(
          $"SELECT name FROM sqlite_master WHERE type='table' AND name='{config.tableName}';"
        )
        |> Seq.tryHead
        |> Result.requireSome "Table not found"
    }

    match operation with
    | Ok value -> Assert.AreEqual(config.tableName, value)
    | Error err -> Assert.Fail($"Failed to Setup the Database: %s{err}")

  [<TestMethod>]
  member _.``Find Migration should find a migration by name``() =
    let operation = validation {
      do! databaseEnv.SetupDatabase()

      let insertStuff () =
        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        DatabaseData.insertMigrationRecords 3 config.tableName connection

      insertStuff() |> printfn "Inserted: %i"

      let! one =
        databaseEnv.FindMigration("test_1")
        |> Result.requireSome "Migration 1 not found"

      and! two =
        databaseEnv.FindMigration("test_2")
        |> Result.requireSome "Migration 2 not found"

      and! three =
        databaseEnv.FindMigration("test_3")
        |> Result.requireSome "Migration 3 not found"

      and! four =
        databaseEnv.FindMigration("test_4")
        |> Result.requireSome "Migration 4 not found"


      return (one, two, three, four)
    }

    match operation with
    | Ok(one, two, three, four) ->
      Assert.AreEqual("test_1", one.name)
      Assert.AreEqual("test_2", two.name)
      Assert.AreEqual("test_3", three.name)
      Assert.AreEqual("test_4", four.name)
    | Error err ->
      let err = String.Join('\n', err)
      Assert.Fail($"Failed to find the migration: %s{err}")