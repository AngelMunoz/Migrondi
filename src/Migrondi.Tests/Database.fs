namespace Migrondi.Tests.Database

open System

open Microsoft.VisualStudio.TestTools.UnitTesting

open RepoDb

open FsToolkit.ErrorHandling

open Microsoft.Extensions.Logging

open Migrondi.Core
open Migrondi.Core.Database


module DatabaseData =
  open System.Data

  let getConfig (dbName: Guid) = {
    connection =
      $"Data Source={IO.Path.Join(IO.Path.GetTempPath(), dbName.ToString())}.db"
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

  let createMigrationQuery tableName =
    $"""CREATE TABLE %s{tableName}(
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      name VARCHAR(255) NOT NULL);""",
    $"""DROP TABLE %s{tableName};"""

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

  let createTestMigrations upToIndex = [
    for i in 0..upToIndex do
      let name = $"test_%i{i + 1}"

      let timestamp =
        DateTimeOffset.UtcNow.AddMinutes(float(i + 1)).ToUnixTimeMilliseconds()

      let upContent, downContent = createMigrationQuery name

      {
        name = name
        timestamp = timestamp
        upContent = upContent
        downContent = downContent
        manualTransaction = false
      }

  ]

[<TestClass>]
type DatabaseTests() =
  let dbName = Guid.NewGuid()
  let config = DatabaseData.getConfig dbName

  let loggerFactory =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.Debug).AddSimpleConsole() |> ignore
    )

  let logger = loggerFactory.CreateLogger("Migrondi:Tests.Database")

  let databaseEnv =
    MiDatabaseHandler(logger, DatabaseData.getConfig dbName)
    :> IMiDatabaseHandler

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
      do databaseEnv.SetupDatabase()

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
      do databaseEnv.SetupDatabase()

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

  [<TestMethod>]
  member _.``FindLastApplied should not find anything if table is empty``() =
    let operation = result {
      do databaseEnv.SetupDatabase()
      let migration = databaseEnv.FindLastApplied()

      do!
        databaseEnv.FindLastApplied()
        |> Result.requireNone "There should be no migrations"

      return migration
    }

    match operation with
    | Ok(None) -> Assert.IsTrue(true)
    | Ok(Some value) ->
      Assert.Fail(
        $"A migration was found though there shouldn't be one: %A{value}"
      )
    | Error err ->
      Assert.Fail(
        $"A migration was found though there shouldn't be one: %s{err}"
      )

  [<TestMethod>]
  member _.``FindLastApplied should find the last migration in the database``
    ()
    =
    let operation = result {
      do databaseEnv.SetupDatabase()

      let insertStuff () =
        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        DatabaseData.insertMigrationRecords 3 config.tableName connection

      insertStuff() |> printfn "Inserted: %i"

      let! result =
        databaseEnv.FindLastApplied()
        |> Result.requireSome "There should be at least 1 migration"

      return result
    }

    match operation with
    | Ok value -> Assert.AreEqual("test_4", value.name)
    | Error err ->
      Assert.Fail($"Failed to find the last applied migration: %s{err}")

  [<TestMethod>]
  member _.``ListMigrations should not show anything if the table is empty``() =
    let operation = result {
      do databaseEnv.SetupDatabase()

      let migrations = databaseEnv.ListMigrations()

      do!
        databaseEnv.ListMigrations()
        |> Result.requireEmpty "There should be no migrations"

      return migrations
    }

    match operation with
    | Ok value -> Assert.AreEqual(0, value.Count)
    | Error err ->
      Assert.Fail($"Failed to find the last applied migration: %s{err}")

  [<TestMethod>]
  member _.``ListMigrations should show existing migrations``() =
    let operation = result {
      do databaseEnv.SetupDatabase()

      let insertStuff () =
        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        DatabaseData.insertMigrationRecords 3 config.tableName connection

      insertStuff() |> printfn "Inserted: %i"

      let migrations = databaseEnv.ListMigrations()

      do!
        databaseEnv.ListMigrations()
        |> Result.requireNotEmpty "The list should contain values"

      return migrations
    }

    // IMPORTANT: The list should be sorted by descending timestamp so
    // the test has to account for the order as well, not just the size of the list
    match operation with
    | Ok value ->
      Assert.AreEqual(4, value.Count)
      Assert.AreEqual("test_4", value[0].name)
      Assert.AreEqual("test_3", value[1].name)
      Assert.AreEqual("test_2", value[2].name)
      Assert.AreEqual("test_1", value[3].name)
    | Error err ->
      Assert.Fail($"Failed to find the last applied migration: %s{err}")

  [<TestMethod>]
  member _.``ApplyMigrations Should apply migrations and return the applied migrations``
    ()
    =
    let operation = result {
      do databaseEnv.SetupDatabase()

      let sampleMigrations = DatabaseData.createTestMigrations 3

      let migrations = databaseEnv.ApplyMigrations(sampleMigrations)

      do! migrations |> Result.requireNotEmpty "The list should contain values"

      let! lastApplied =
        databaseEnv.FindLastApplied()
        |> Result.requireSome "There should be at least 1 migration"


      let queryMigratedTables = validation {
        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        let msg = "There should not be records in this table"

        do!
          connection.QueryAll<{| name: string |}>(tableName = "test_1")
          |> Result.requireEmpty msg

        do!
          connection.QueryAll<{| name: string |}>(tableName = "test_2")
          |> Result.requireEmpty msg

        do!
          connection.QueryAll<{| name: string |}>(tableName = "test_3")
          |> Result.requireEmpty msg

        do!
          connection.QueryAll<{| name: string |}>(tableName = "test_4")
          |> Result.requireEmpty msg
      }

      do!
        queryMigratedTables
        |> Result.mapError(fun err -> String.Join("\n", err))


      return (migrations, lastApplied)
    }

    match operation with
    | Ok(migrations, lastApplied) ->
      Assert.AreEqual(4, migrations.Count)
      Assert.AreEqual("test_4", migrations[0].name)
      Assert.AreEqual("test_3", migrations[1].name)
      Assert.AreEqual("test_2", migrations[2].name)
      Assert.AreEqual("test_1", migrations[3].name)
      Assert.AreEqual("test_4", lastApplied.name)
    | Error err ->
      Assert.Fail($"Failed to find the last applied migration: %s{err}")

  [<TestMethod>]
  member _.``RollBackMigrations Should roll back migrations and return the migrations that were reverted from the database``
    ()
    =
    let operation = result {
      do databaseEnv.SetupDatabase()

      let sampleMigrations = DatabaseData.createTestMigrations 3

      // Fill the Database with sample migrations
      do databaseEnv.ApplyMigrations(sampleMigrations) |> ignore

      // rollback two
      let afterMigrationRollback =
        databaseEnv.RollbackMigrations(
          // Rollback the last two migrations
          sampleMigrations |> List.rev |> List.take 2
        )

      let! lastApplied =
        databaseEnv.FindLastApplied()
        |> Result.requireSome "There should be at least 1 migration"

      let queryMigratedTables = validation {
        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        let! _ =
          try
            connection.QueryAll<{| name: string |}>(tableName = "test_3")
            |> ignore

            Error "Table 'test_3' should not exist"
          with ex ->
            Ok(ex.Message)

        and! _ =
          try
            connection.QueryAll<{| name: string |}>(tableName = "test_4")
            |> ignore

            Error "Table 'test_4' should not exist"
          with ex ->
            Ok(ex.Message)

        return ()
      }

      do!
        queryMigratedTables
        |> Result.mapError(fun err -> String.Join("\n", err))

      return (afterMigrationRollback, lastApplied)
    }

    match operation with
    | Ok(migrations, lastApplied) ->
      Assert.AreEqual(2, migrations.Count)
      Assert.AreEqual("test_4", migrations[0].name)
      Assert.AreEqual("test_3", migrations[1].name)
    | Error err ->
      Assert.Fail($"Failed to find the last applied migration: %s{err}")

  [<TestMethod>]
  member _.``ApplyMigrations should stop applying migrations as soon as one fails``
    ()
    =
    do databaseEnv.SetupDatabase()

    let sampleMigrations = DatabaseData.createTestMigrations 4

    let failingMigration = {
      name = "fail-migration"
      timestamp = DateTimeOffset.Now.AddMinutes(2.).ToUnixTimeMilliseconds()
      upContent = "create table test_1();"
      downContent = "drop table test_5;"
      manualTransaction = false
    }

    let runnableMigrations = [
      sampleMigrations[0]
      sampleMigrations[1]
      failingMigration // add failing migration
      // these will not be applied at all
      sampleMigrations[2]
      sampleMigrations[3]
      sampleMigrations[4]

    ]

    let thrown =
      Assert.ThrowsException<MigrationApplicationFailed>(
        Action(
          (fun () -> databaseEnv.ApplyMigrations(runnableMigrations) |> ignore)
        )
      )

    Assert.AreEqual(failingMigration, thrown.Migration)

    match databaseEnv.FindLastApplied() with
    | Some migration -> Assert.AreEqual("test_2", migration.name)
    | None -> Assert.Fail("There should be a migration in the database")


  [<TestMethod>]
  member _.``RollbackMigrations should stop rolling back migrations as soon as one fails``
    ()
    =
    do databaseEnv.SetupDatabase()

    let sampleMigrations = DatabaseData.createTestMigrations 4

    // pre-fill the database
    do databaseEnv.ApplyMigrations(sampleMigrations) |> ignore

    let failingMigration = {
      name = "fail-migration"
      timestamp = DateTimeOffset.Now.AddMinutes(2.).ToUnixTimeMilliseconds()
      upContent = "create table test_1();"
      downContent = "drop table test_5;"
      manualTransaction = false
    }

    let runnableMigrations = [
      let sampleMigrations = sampleMigrations |> List.rev
      sampleMigrations[0]
      sampleMigrations[1]
      failingMigration // add failing migration
      // these migrations should not be rolled back at all
      sampleMigrations[2]
      sampleMigrations[3]
      sampleMigrations[4]
    ]

    let thrown =
      Assert.ThrowsException<MigrationRollbackFailed>(
        Action(
          (fun () ->
            databaseEnv.RollbackMigrations(runnableMigrations) |> ignore
          )
        )
      )

    Assert.AreEqual(failingMigration, thrown.Migration)

    match databaseEnv.FindLastApplied() with
    | Some migration -> Assert.AreEqual("test_3", migration.name)
    | None -> Assert.Fail($"Failed to find the last applied migration")

  [<TestMethod>]
  member _.``Migrations with ManualTransaction can be applied``() =
    databaseEnv.SetupDatabase()

    let applied =
      databaseEnv.ApplyMigrations(
        [
          {
            name = "manual-transaction"
            timestamp =
              DateTimeOffset.Now.AddMinutes(2.).ToUnixTimeMilliseconds()
            upContent =
              """create table test_1(value int);
begin transaction;
insert into test_1 (value) values (1);
commit;
"""
            downContent = "drop table test_1;"
            manualTransaction = true
          }
        ]
      )

    Assert.AreEqual(1, applied.Count)