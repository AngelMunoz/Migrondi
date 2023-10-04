namespace Migrondi.Tests.Database

open System
open System.Threading.Tasks

open Microsoft.VisualStudio.TestTools.UnitTesting

open Microsoft.Extensions.Logging

open RepoDb

open FsToolkit.ErrorHandling

open Migrondi.Core
open Migrondi.Core.Database

open Migrondi.Tests.Database


[<TestClass>]
type DatabaseAsyncTests() =

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
    databaseEnv.SetupDatabase()


  [<TestCleanup>]
  member _.TestCleanup() =
    loggerFactory.Dispose()
    IO.File.Delete($"./{dbName.ToString()}.db")


  [<TestMethod>]
  member _.``Database Should be Setup Correctly async``() =
    task {
      let! operation = asyncResult {

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

    }
    :> Task

  [<TestMethod>]
  member _.``Find Migration should find a migration by name``() =
    task {
      let! operation = asyncResult {

        let insertStuff () =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          DatabaseData.insertMigrationRecords 3 config.tableName connection

        insertStuff() |> printfn "Inserted: %i"

        let! one =
          databaseEnv.FindMigrationAsync("test_1")
          |> TaskResult.requireSome "Migration 1 not found"

        and! two =
          databaseEnv.FindMigrationAsync("test_2")
          |> TaskResult.requireSome "Migration 2 not found"

        and! three =
          databaseEnv.FindMigrationAsync("test_3")
          |> TaskResult.requireSome "Migration 3 not found"

        and! four =
          databaseEnv.FindMigrationAsync("test_4")
          |> TaskResult.requireSome "Migration 4 not found"

        return (one, two, three, four)
      }

      match operation with
      | Ok(one, two, three, four) ->
        Assert.AreEqual("test_1", one.name)
        Assert.AreEqual("test_2", two.name)
        Assert.AreEqual("test_3", three.name)
        Assert.AreEqual("test_4", four.name)
      | Error err -> Assert.Fail($"Failed to Find the Migration: %s{err}")
    }
    :> Task

  [<TestMethod>]
  member _.``FindLastApplied should not find anything if table is empty``() =
    task {
      let! operation = asyncResult {
        let! migration = databaseEnv.FindLastAppliedAsync()

        do!
          migration
          |> Result.requireNone "Found a migration when it shouldn't have"

        return migration
      }

      match operation with
      | Ok None -> Assert.IsTrue true
      | Ok(Some value) ->
        Assert.Fail(
          $"A migration was found though there shouldn't be one: %A{value}"
        )
      | Error err ->
        Assert.Fail(
          $"A migration was found though there shouldn't be one: %s{err}"
        )

    }
    :> Task

  [<TestMethod>]
  member _.``FindLastApplied should find the last migration in the database``
    ()
    =
    task {
      let! operation = asyncResult {

        let insertStuff () =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          DatabaseData.insertMigrationRecords 3 config.tableName connection

        insertStuff() |> printfn "Inserted: %i"

        let! migration =
          databaseEnv.FindLastAppliedAsync()
          |> TaskResult.requireSome
            "Didn't find a migration when it should have"

        return migration
      }

      match operation with
      | Ok value -> Assert.AreEqual("test_4", value.name)
      | Error err ->
        Assert.Fail(
          $"A migration was not found though there should be one: %s{err}"
        )

    }
    :> Task

  [<TestMethod>]
  member _.``ListMigrations should not show anything if the table is empty``() =
    task {
      let! operation = asyncResult {

        let! migrations = databaseEnv.ListMigrationsAsync()

        do!
          migrations
          |> Result.requireEmpty "Found migrations when it shouldn't have"

        return migrations
      }

      match operation with
      | Ok value -> Assert.AreEqual(0, value.Count)
      | Error err ->
        Assert.Fail(
          $"Migrations were found though there shouldn't be any: %s{err}"
        )


    }
    :> Task

  [<TestMethod>]
  member _.``ListMigrations should show existing migrations``() =
    task {
      let! operation = asyncResult {

        let insertStuff () =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          DatabaseData.insertMigrationRecords 3 config.tableName connection

        insertStuff() |> printfn "Inserted: %i"

        let! migrations = databaseEnv.ListMigrationsAsync()

        do!
          migrations
          |> Result.requireNotEmpty "Didn't find migrations when it should have"

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
        Assert.Fail(
          $"Migrations were not found though there should be some: %s{err}"
        )

    }
    :> Task

  [<TestMethod>]
  member _.``ApplyMigrations Should apply migrations and return the applied migrations``
    ()
    =
    task {
      let! operation = asyncResult {

        let sampleMigrations = DatabaseData.createTestMigrations 3

        let! migrations = databaseEnv.ApplyMigrationsAsync(sampleMigrations)

        do!
          migrations |> Result.requireNotEmpty "The list should contain values"

        let! lastApplied =
          databaseEnv.FindLastAppliedAsync()
          |> TaskResult.requireSome "There should be at least 1 migration"

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
    }
    :> Task

  [<TestMethod>]
  member _.``RollBackMigrations Should rollback migrations and return the migrations left in the database``
    ()
    =
    task {
      let! operation = asyncResult {

        let sampleMigrations = DatabaseData.createTestMigrations 3

        // pre-fill the Database with sample migrations
        do!
          databaseEnv.ApplyMigrationsAsync(sampleMigrations)
          |> Async.AwaitTask
          |> Async.Ignore

        // rollback two
        let! afterMigrationRollback =
          databaseEnv.RollbackMigrationsAsync(
            // Rollback the last two migrations
            sampleMigrations |> List.rev |> List.take 2
          )

        let! lastApplied =
          databaseEnv.FindLastAppliedAsync()
          |> TaskResult.requireSome "There should be at least 1 migration"

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
        Assert.AreEqual("test_2", migrations[0].name)
        Assert.AreEqual("test_1", migrations[1].name)
        Assert.AreEqual("test_2", lastApplied.name)
      | Error err ->
        Assert.Fail($"Failed to find the last applied migration: %s{err}")
    }
    :> Task


  [<TestMethod>]
  member _.``ApplyMigrations should stop applying migrations as soon as one fails``
    ()
    =
    task {


      let sampleMigrations = DatabaseData.createTestMigrations 4

      let failingMigration = {
        name = "fail-migration"
        timestamp = DateTimeOffset.Now.AddMinutes(2.).ToUnixTimeMilliseconds()
        upContent = "create table test_1();"
        downContent = "drop table test_5;"
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

      let! thrown =
        Assert.ThrowsExceptionAsync<AggregateException>(
          Func<Task>(fun _ -> task {
            do!
              databaseEnv.ApplyMigrationsAsync(runnableMigrations)
              |> Async.AwaitTask
              |> Async.Ignore
          })
        )

      let thrown =
        thrown.InnerExceptions
        |> Seq.tryPick(fun ex ->
          match ex with
          | :? MigrationApplicationFailed as ae -> Some ae
          | _ -> None
        )
        |> Option.defaultWith(fun () ->
          failwith "MigrationApplicationFailed Not found in inner exceptions"
        )

      Assert.AreEqual(failingMigration, thrown.Migration)

      match! databaseEnv.FindLastAppliedAsync() with
      | Some migration -> Assert.AreEqual("test_2", migration.name)
      | None -> Assert.Fail("Failed to find the last applied migration")
    }
    :> Task

  [<TestMethod>]
  member _.``RollbackMigrations should stop rolling back migrations as soon as one fails``
    ()
    =
    task {


      let sampleMigrations = DatabaseData.createTestMigrations 4

      // pre-fill the database
      do!
        databaseEnv.ApplyMigrationsAsync(sampleMigrations)
        |> Async.AwaitTask
        |> Async.Ignore

      let failingMigration = {
        name = "fail-migration"
        timestamp = DateTimeOffset.Now.AddMinutes(2.).ToUnixTimeMilliseconds()
        upContent = "create table test_1();"
        downContent = "drop table test_5;"
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

      let! thrown =
        Assert.ThrowsExceptionAsync<AggregateException>(
          Func<Task>(fun _ -> task {
            do!
              databaseEnv.RollbackMigrationsAsync(runnableMigrations)
              |> Async.AwaitTask
              |> Async.Ignore
          })
        )

      let thrown =
        thrown.InnerExceptions
        |> Seq.tryPick(fun ex ->
          match ex with
          | :? MigrationRollbackFailed as ae -> Some ae
          | _ -> None
        )
        |> Option.defaultWith(fun () ->
          failwith "MigrationRollbackFailed Not found in inner exceptions"
        )

      Assert.AreEqual(failingMigration, thrown.Migration)

      match! databaseEnv.FindLastAppliedAsync() with
      | Some migration -> Assert.AreEqual("test_3", migration.name)
      | None -> Assert.Fail("Failed to find the last applied migration")
    }
    :> Task