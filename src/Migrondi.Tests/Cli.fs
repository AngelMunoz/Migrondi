namespace Migrondi.Tests.Cli

open System
open System.Collections.Generic
open System.IO
open System.Threading.Tasks

open Microsoft.Extensions.Logging.Abstractions
open Microsoft.VisualStudio.TestTools.UnitTesting

open Migrondi.Core
open Migrondi.Core.Database
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization
open Migrondi.Commands
open Migrondi.Env
open Migrondi.Handlers

/// Arguments recorded from a RunNew call on the fake below.
type RecordedRunNew = {
  Name: string
  UpContent: string option
  DownContent: string option
  ManualTransaction: bool option
}

/// Records the arguments the CLI handlers pass to the core service.
type FakeMigrondi
  (
    ?statuses: MigrationStatus list,
    ?dryUp: Migration list,
    ?dryDown: Migration list,
    ?statusFor: MigrationStatus
  ) =
  let mutable runNewArgs: RecordedRunNew option = None
  let mutable runUpAmounts: int option list = []
  let mutable runDownAmounts: int option list = []

  member _.RunNewArgs = runNewArgs
  member _.RunUpAmounts = runUpAmounts
  member _.RunDownAmounts = runDownAmounts

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
      runUpAmounts <- runUpAmounts @ [ amount ]
      List<MigrationRecord>() :> IReadOnlyList<_>

    member _.RunDown(?amount) =
      runDownAmounts <- runDownAmounts @ [ amount ]
      List<MigrationRecord>() :> IReadOnlyList<_>

    member _.DryRunUp(?amount) =
      List<Migration>(defaultArg dryUp []) :> IReadOnlyList<_>

    member _.DryRunDown(?amount) =
      List<Migration>(defaultArg dryDown []) :> IReadOnlyList<_>

    member _.MigrationsList() =
      List<MigrationStatus>(defaultArg statuses []) :> IReadOnlyList<_>

    member _.ScriptStatus(_migrationPath) =
      match statusFor with
      | Some status -> status
      | None -> raise(SourceNotFound("path", "name"))

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
  member _.``resolveNewContent returns Error for an unreadable file``() =
    let missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.sql")

    let result =
      Migrations.resolveNewContent(
        TextReader.Null,
        {
          Up = None
          Down = None
          UpFile = Some missing
          DownFile = None
        }
      )

    match result with
    | Ok _ -> Assert.Fail("expected a read error")
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


[<TestClass>]
type TargetTests() =

  let logger = NullLogger.Instance :> Microsoft.Extensions.Logging.ILogger

  let migration(name: string, timestamp: int64) : Migration = {
    name = name
    timestamp = timestamp
    upContent = "SELECT 1;"
    downContent = "SELECT 0;"
    manualTransaction = false
  }

  let a = migration("100_a.sql", 100L)
  let b = migration("200_b.sql", 200L)
  let c = migration("300_c.sql", 300L)

  [<TestMethod>]
  member _.``resolveTarget matches name filename and timestamp``() =
    let statuses = [ Pending c; Pending b; Pending a ]

    for target in [ "b"; "200_b"; "200_b.sql"; "200" ] do
      match Migrations.resolveTarget(statuses, target) with
      | Ok(Pending found) ->
        Assert.AreEqual(200L, found.timestamp, $"target: {target}")
      | Ok(Applied _) -> Assert.Fail($"expected pending for {target}")
      | Error message -> Assert.Fail(message)

    match Migrations.resolveTarget(statuses, "missing") with
    | Ok _ -> Assert.Fail("expected an unknown target error")
    | Error _ -> ()

  [<TestMethod>]
  member _.``upTargetAmount includes the target and stops there``() =
    let pending = [ a; b; c ]

    match Migrations.upTargetAmount(Pending b, pending) with
    | Ok amount -> Assert.AreEqual(2, amount)
    | Error message -> Assert.Fail(message)

    match Migrations.upTargetAmount(Applied b, pending) with
    | Ok amount -> Assert.AreEqual(0, amount)
    | Error message -> Assert.Fail(message)

  [<TestMethod>]
  member _.``downTargetAmount leaves the target applied``() =
    let applied = [ c; b; a ]

    match Migrations.downTargetAmount(Applied b, applied) with
    | Ok amount -> Assert.AreEqual(1, amount)
    | Error message -> Assert.Fail(message)

    match Migrations.downTargetAmount(Applied c, applied) with
    | Ok amount -> Assert.AreEqual(0, amount)
    | Error message -> Assert.Fail(message)

    match Migrations.downTargetAmount(Pending b, applied) with
    | Ok _ -> Assert.Fail("expected a not-applied error")
    | Error _ -> ()

  [<TestMethod>]
  member _.``runTargetUp runs the derived amount``() =
    let fake =
      FakeMigrondi(
        statuses = [ Pending c; Pending b; Pending a ],
        dryUp = [ a; b; c ]
      )

    let exitCode =
      Migrations.runTargetUp(
        logger,
        fake :> IMigrondi,
        { Target = "b"; DryRun = false }
      )

    Assert.AreEqual(0, exitCode)
    Assert.AreEqual<int option list>([ Some 2 ], fake.RunUpAmounts)

  [<TestMethod>]
  member _.``runTargetUp unknown target fails without running``() =
    let fake = FakeMigrondi(statuses = [ Pending a ], dryUp = [ a ])

    let exitCode =
      Migrations.runTargetUp(
        logger,
        fake :> IMigrondi,
        { Target = "missing"; DryRun = false }
      )

    Assert.AreEqual(1, exitCode)
    Assert.IsTrue(fake.RunUpAmounts.IsEmpty)

  [<TestMethod>]
  member _.``upTargetAmount counts from the oldest pending migration``() =
    let pending = [ a; b; c ]

    match Migrations.upTargetAmount(Pending a, pending) with
    | Ok amount -> Assert.AreEqual(1, amount)
    | Error message -> Assert.Fail(message)

    match Migrations.upTargetAmount(Pending c, pending) with
    | Ok amount -> Assert.AreEqual(3, amount)
    | Error message -> Assert.Fail(message)

  [<TestMethod>]
  member _.``downTargetAmount rolls back everything newer than the target``() =
    let applied = [ c; b; a ]

    match Migrations.downTargetAmount(Applied a, applied) with
    | Ok amount -> Assert.AreEqual(2, amount)
    | Error message -> Assert.Fail(message)

  [<TestMethod>]
  member _.``runTargetDown rolls back through the target``() =
    let fake =
      FakeMigrondi(
        statuses = [ Applied c; Applied b; Applied a ],
        dryDown = [ c; b; a ]
      )

    let exitCode =
      Migrations.runTargetDown(
        logger,
        fake :> IMigrondi,
        { Target = "b"; DryRun = false }
      )

    Assert.AreEqual(0, exitCode)
    Assert.AreEqual<int option list>([ Some 1 ], fake.RunDownAmounts)

  [<TestMethod>]
  member _.``runTargetUp dry run selects without applying``() =
    let fake =
      FakeMigrondi(
        statuses = [ Pending c; Pending b; Pending a ],
        dryUp = [ a; b; c ]
      )

    let exitCode =
      Migrations.runTargetUp(
        logger,
        fake :> IMigrondi,
        { Target = "b"; DryRun = true }
      )

    Assert.AreEqual(0, exitCode)
    Assert.IsTrue(fake.RunUpAmounts.IsEmpty)

  [<TestMethod>]
  member _.``runTargetDown dry run selects without rolling back``() =
    let fake =
      FakeMigrondi(
        statuses = [ Applied c; Applied b; Applied a ],
        dryDown = [ c; b; a ]
      )

    let exitCode =
      Migrations.runTargetDown(
        logger,
        fake :> IMigrondi,
        { Target = "b"; DryRun = true }
      )

    Assert.AreEqual(0, exitCode)
    Assert.IsTrue(fake.RunDownAmounts.IsEmpty)


[<TestClass>]
type StatusExitTests() =

  let logger = NullLogger.Instance :> Microsoft.Extensions.Logging.ILogger

  let pending: MigrationStatus =
    Pending {
      name = "100_a.sql"
      timestamp = 100L
      upContent = "SELECT 1;"
      downContent = "SELECT 0;"
      manualTransaction = false
    }

  [<TestMethod>]
  member _.``migrationStatus returns 0 for a known migration``() =
    let fake = FakeMigrondi(statusFor = pending)

    Assert.AreEqual(
      0,
      Migrations.migrationStatus("a", logger, fake :> IMigrondi)
    )

  [<TestMethod>]
  member _.``migrationStatus returns 1 for an unknown migration``() =
    let fake = FakeMigrondi()

    Assert.AreEqual(
      1,
      Migrations.migrationStatus("missing", logger, fake :> IMigrondi)
    )


[<TestClass>]
type ArgumentMapperTests() =

  let logger = NullLogger.Instance :> Microsoft.Extensions.Logging.ILogger

  let buildEnv(fake: FakeMigrondi) =
    let serializer = MigrondiSerializer()
    let config = MigrondiConfig.Default

    let fileSystem =
      MiFileSystem(
        logger,
        serializer,
        serializer,
        Uri(Path.GetTempPath(), UriKind.Absolute),
        Uri("migrations/", UriKind.Relative)
      )

    let database = MiDatabaseHandler(logger, config)

    AppEnv(
      logger,
      serializer :> IMiConfigurationSerializer,
      serializer :> IMiMigrationSerializer,
      fileSystem :> IMiFileSystem,
      database :> IMiDatabaseHandler,
      fake :> IMigrondi,
      false,
      config
    )

  [<TestMethod>]
  member _.``up rejects amount together with to``() =
    let fake = FakeMigrondi()
    let appEnv = buildEnv fake

    let exitCode = ArgumentMapper.Up appEnv (Some 1, None, Some "200_b")

    Assert.AreEqual(1, exitCode)
    Assert.IsTrue(fake.RunUpAmounts.IsEmpty)

  [<TestMethod>]
  member _.``down rejects amount together with to``() =
    let fake = FakeMigrondi()
    let appEnv = buildEnv fake

    let exitCode = ArgumentMapper.Down appEnv (Some 1, None, Some "200_b")

    Assert.AreEqual(1, exitCode)
    Assert.IsTrue(fake.RunDownAmounts.IsEmpty)
