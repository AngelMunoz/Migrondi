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
type FakeMigrondi() =
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
      List<MigrationStatus>() :> IReadOnlyList<_>

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
      Task.FromResult(List<MigrationStatus>() :> IReadOnlyList<_>)

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
