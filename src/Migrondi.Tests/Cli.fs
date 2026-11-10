namespace Migrondi.Tests.Cli

open System
open System.Collections.Generic
open System.IO
open System.Threading.Tasks

open Microsoft.Extensions.Logging.Abstractions
open Microsoft.VisualStudio.TestTools.UnitTesting

open Migrondi.Core
open Migrondi.Handlers

/// Arguments recorded from a RunNew call on the fake below.
type RecordedRunNew = {
  Name: string
  UpContent: string option
  DownContent: string option
  ManualTransaction: bool option
}

/// Records the arguments the CLI handlers pass to the core service.
type FakeMigrondi(?statuses: MigrationStatus list) =
  let mutable runNewArgs: RecordedRunNew option = None

  member _.RunNewArgs = runNewArgs

  interface IMigrondi with
    member _.Initialize() = ()
    member _.InitializeAsync(?cancellationToken) = Task.CompletedTask

    member _.RunNew
      (friendlyName, ?upContent, ?downContent, ?manualTransaction)
      =
      runNewArgs <-
        Some {
          Name = friendlyName
          UpContent = upContent
          DownContent = downContent
          ManualTransaction = manualTransaction
        }

      {
        name = "1_test.sql"
        timestamp = 1L
        upContent = defaultArg upContent ""
        downContent = defaultArg downContent ""
        manualTransaction = defaultArg manualTransaction false
      }

    member _.RunNewAsync
      (
        friendlyName,
        ?upContent,
        ?downContent,
        ?manualTransaction,
        ?cancellationToken
      ) =
      Task.FromResult {
        name = "1_test.sql"
        timestamp = 1L
        upContent = defaultArg upContent ""
        downContent = defaultArg downContent ""
        manualTransaction = defaultArg manualTransaction false
      }

    member _.RunUp(?amount) =
      List<MigrationRecord>() :> IReadOnlyList<_>

    member _.RunDown(?amount) =
      List<MigrationRecord>() :> IReadOnlyList<_>

    member _.DryRunUp(?amount) = List<Migration>() :> IReadOnlyList<_>
    member _.DryRunDown(?amount) = List<Migration>() :> IReadOnlyList<_>

    member _.MigrationsList() =
      List<MigrationStatus>(defaultArg statuses []) :> IReadOnlyList<_>

    member _.ScriptStatus(_migrationPath) = failwith "not used"

    member _.RunUpAsync(?amount, ?cancellationToken) =
      Task.FromResult(List<MigrationRecord>() :> IReadOnlyList<_>)

    member _.RunDownAsync(?amount, ?cancellationToken) =
      Task.FromResult(List<MigrationRecord>() :> IReadOnlyList<_>)

    member _.DryRunUpAsync(?amount, ?cancellationToken) =
      Task.FromResult(List<Migration>() :> IReadOnlyList<_>)

    member _.DryRunDownAsync(?amount, ?cancellationToken) =
      Task.FromResult(List<Migration>() :> IReadOnlyList<_>)

    member _.MigrationsListAsync(?cancellationToken) =
      Task.FromResult(
        List<MigrationStatus>(defaultArg statuses []) :> IReadOnlyList<_>
      )

    member _.ScriptStatusAsync(_migrationPath, ?cancellationToken) =
      failwith "not used"


[<TestClass>]
type NewContentTests() =

  let logger = NullLogger.Instance :> Microsoft.Extensions.Logging.ILogger

  [<TestMethod>]
  member _.``resolveNewContent passes inline content through``() =
    let result =
      Migrations.resolveNewContent(
        TextReader.Null,
        {
          Up = Some "CREATE TABLE t (id INT);"
          Down = Some "DROP TABLE t;"
          UpFile = None
          DownFile = None
        }
      )

    match result with
    | Ok resolved ->
      Assert.AreEqual<string option>(
        Some "CREATE TABLE t (id INT);",
        resolved.UpContent
      )

      Assert.AreEqual<string option>(Some "DROP TABLE t;", resolved.DownContent)
    | Error message -> Assert.Fail(message)

  [<TestMethod>]
  member _.``resolveNewContent reads file and stdin content``() =
    let path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sql")

    File.WriteAllText(path, "CREATE TABLE from_file (id INT);")

    try
      use reader = new StringReader("DROP TABLE from_stdin;")

      let result =
        Migrations.resolveNewContent(
          reader,
          {
            Up = None
            Down = None
            UpFile = Some path
            DownFile = Some "-"
          }
        )

      match result with
      | Ok resolved ->
        Assert.AreEqual<string option>(
          Some "CREATE TABLE from_file (id INT);",
          resolved.UpContent
        )

        Assert.AreEqual<string option>(
          Some "DROP TABLE from_stdin;",
          resolved.DownContent
        )
      | Error message -> Assert.Fail(message)
    finally
      File.Delete(path)

  [<TestMethod>]
  member _.``resolveNewContent rejects inline and file for same section``() =
    let result =
      Migrations.resolveNewContent(
        TextReader.Null,
        {
          Up = Some "CREATE TABLE t (id INT);"
          Down = None
          UpFile = Some "other.sql"
          DownFile = None
        }
      )

    match result with
    | Ok _ -> Assert.Fail("expected a conflict error")
    | Error _ -> ()

  [<TestMethod>]
  member _.``resolveNewContent rejects stdin for both sections``() =
    use reader = new StringReader("SELECT 1;")

    let result =
      Migrations.resolveNewContent(
        reader,
        {
          Up = None
          Down = None
          UpFile = Some "-"
          DownFile = Some "-"
        }
      )

    match result with
    | Ok _ -> Assert.Fail("expected a stdin reuse error")
    | Error _ -> ()

  [<TestMethod>]
  member _.``newMigration passes resolved content to RunNew``() =
    let fake = FakeMigrondi()

    let exitCode =
      Migrations.newMigration(
        logger,
        fake :> IMigrondi,
        {
          Name = "add_users"
          ManualTransaction = None
          Content = {
            Up = Some "CREATE TABLE users (id INT);"
            Down = Some "DROP TABLE users;"
            UpFile = None
            DownFile = None
          }
          Stdin = TextReader.Null
        }
      )

    Assert.AreEqual(0, exitCode)

    match fake.RunNewArgs with
    | Some recorded ->
      Assert.AreEqual("add_users", recorded.Name)

      Assert.AreEqual<string option>(
        Some "CREATE TABLE users (id INT);",
        recorded.UpContent
      )

      Assert.AreEqual<string option>(
        Some "DROP TABLE users;",
        recorded.DownContent
      )
    | None -> Assert.Fail("RunNew was not called")

  [<TestMethod>]
  member _.``newMigration conflict returns 1 without calling RunNew``() =
    let fake = FakeMigrondi()

    let exitCode =
      Migrations.newMigration(
        logger,
        fake :> IMigrondi,
        {
          Name = "add_users"
          ManualTransaction = None
          Content = {
            Up = Some "CREATE TABLE users (id INT);"
            Down = None
            UpFile = Some "other.sql"
            DownFile = None
          }
          Stdin = TextReader.Null
        }
      )

    Assert.AreEqual(1, exitCode)
    Assert.IsTrue(fake.RunNewArgs.IsNone)


[<TestClass>]
type ListFilterTests() =

  let migration(name: string, timestamp: int64, up: string, applied: bool) =
    let migration: Migration = {
      name = name
      timestamp = timestamp
      upContent = up
      downContent = $"DROP {name};"
      manualTransaction = false
    }

    if applied then Applied migration else Pending migration

  // newest first, as MigrationsList returns them
  let statuses = [
    migration("300_c.sql", 300L, "CREATE TABLE c (id INT);", true)
    migration("200_b.sql", 200L, "CREATE TABLE b (id INT);", false)
    migration("100_a.sql", 100L, "ALTER TABLE users ADD email TEXT;", false)
  ]

  let filters: Migrations.ListFilters = {
    Kind = None
    Pending = None
    Applied = None
    Limit = None
    Last = None
    Search = None
    ShowPath = false
    ShowContent = false
  }

  let names(result: Result<MigrationStatus list, string>) =
    match result with
    | Ok selected -> selected |> List.map(fun status -> status.Value.name)
    | Error message ->
      Assert.Fail(message)
      []

  [<TestMethod>]
  member _.``filterMigrations pending and applied select by status``() =
    Assert.AreEqual<string list>(
      [ "200_b.sql"; "100_a.sql" ],
      names(
        Migrations.filterMigrations(
          { filters with Pending = Some true },
          statuses
        )
      )
    )

    Assert.AreEqual<string list>(
      [ "300_c.sql" ],
      names(
        Migrations.filterMigrations(
          { filters with Applied = Some true },
          statuses
        )
      )
    )

  [<TestMethod>]
  member _.``filterMigrations rejects contradictory status flags``() =
    match
      Migrations.filterMigrations(
        {
          filters with
              Pending = Some true
              Applied = Some true
        },
        statuses
      )
    with
    | Ok _ -> Assert.Fail("expected a conflict error")
    | Error _ -> ()

    match
      Migrations.filterMigrations(
        {
          filters with
              Kind = Some MigrationType.Up
              Pending = Some true
        },
        statuses
      )
    with
    | Ok _ -> Assert.Fail("expected a conflict error")
    | Error _ -> ()

  [<TestMethod>]
  member _.``filterMigrations search matches name and content ignoring case``
    ()
    =
    Assert.AreEqual<string list>(
      [ "100_a.sql" ],
      names(
        Migrations.filterMigrations(
          {
            filters with
                Search = Some "add email"
          },
          statuses
        )
      )
    )

    Assert.AreEqual<string list>(
      [ "200_b.sql" ],
      names(
        Migrations.filterMigrations(
          { filters with Search = Some "200_B" },
          statuses
        )
      )
    )

  [<TestMethod>]
  member _.``filterMigrations limit takes newest and last takes oldest``() =
    Assert.AreEqual<string list>(
      [ "300_c.sql" ],
      names(
        Migrations.filterMigrations({ filters with Limit = Some 1 }, statuses)
      )
    )

    Assert.AreEqual<string list>(
      [ "100_a.sql" ],
      names(
        Migrations.filterMigrations({ filters with Last = Some 1 }, statuses)
      )
    )

  [<TestMethod>]
  member _.``filterMigrations rejects limit with last and non-positive counts``
    ()
    =
    for filters in
      [
        {
          filters with
              Limit = Some 1
              Last = Some 1
        }
        { filters with Limit = Some 0 }
        { filters with Last = Some -2 }
      ] do
      match Migrations.filterMigrations(filters, statuses) with
      | Ok _ -> Assert.Fail("expected an error")
      | Error _ -> ()

  [<TestMethod>]
  member _.``migrationFileName reconstructs V1 and V0 file names``() =
    let v1Applied = statuses |> List.head

    let v0: Migration = {
      name = "a_100"
      timestamp = 100L
      upContent = ""
      downContent = ""
      manualTransaction = false
    }

    match v1Applied with
    | Applied migration ->
      Assert.AreEqual("300_c.sql", Migrations.migrationFileName migration)
    | Pending _ -> Assert.Fail("expected an applied migration")

    Assert.AreEqual("a_100.sql", Migrations.migrationFileName v0)

    Assert.AreEqual(
      "300_c.sql",
      Migrations.migrationFileName {
        v0 with
            name = "300_c"
            timestamp = 300L
      }
    )

  [<TestMethod>]
  member _.``filterMigrations combines status search and limit``() =
    Assert.AreEqual<string list>(
      [ "200_b.sql" ],
      names(
        Migrations.filterMigrations(
          {
            filters with
                Pending = Some true
                Search = Some "create table"
                Limit = Some 1
          },
          statuses
        )
      )
    )
