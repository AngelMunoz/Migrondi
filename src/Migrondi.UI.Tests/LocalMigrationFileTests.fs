module Migrondi.UI.Tests.LocalMigrationFileTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks

open Xunit

open Microsoft.Extensions.Logging

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Migrondi.Core
open MigrondiUI.Services
open Migrondi.UI.Services

// A local project whose single migration carries the legacy timestamped
// NAME Core's RunNew used to write; mirrors a real pre-fix file on disk.
let arrangeLegacyProject() =
  let tmp =
    Path.Combine(Path.GetTempPath(), $"migrondi-local-{Guid.NewGuid():N}")

  let migrationsDir = Path.Combine(tmp, "migrations")
  Directory.CreateDirectory migrationsDir |> ignore

  let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
  let fileName = $"{timestamp}_legacy_case.sql"

  let content =
    $"-- MIGRONDI:NAME={timestamp}_legacy_case.sql\n-- MIGRONDI:TIMESTAMP={timestamp}\n-- ---------- MIGRONDI:UP ----------\nCREATE TABLE legacy_case (id INTEGER);\n-- ---------- MIGRONDI:DOWN ----------\nDROP TABLE legacy_case;\n"

  File.WriteAllText(Path.Combine(migrationsDir, fileName), content)

  let loggerFactory = LoggerFactory.Create(fun _ -> ())

  let dbFile = Path.Combine(tmp, "t.db")

  let config: MigrondiConfig = {
    connection = $"Data Source={dbFile}"
    migrations = "migrations"
    tableName = "migrations"
    driver = MigrondiDriver.Sqlite
  }

  let core =
    Migrondi.MigrondiFactory(
      config,
      tmp,
      loggerFactory.CreateLogger<IMigrondi>()
    )

  let ops =
    MigrationOperations(
      core,
      Guid.NewGuid(),
      (fun () -> Unchecked.defaultof<System.Data.IDbConnection>),
      false,
      migrationsDir
    )
    :> IMigrationOperations

  tmp, migrationsDir, timestamp, fileName, ops

let runAsync(work: Async<unit>) : Task =
  Async.StartAsTask(work, cancellationToken = CancellationToken.None) :> Task

[<Fact>]
let ``GetMigration finds a legacy file by its decoded name``() : Task =
  let _, _, timestamp, _, ops = arrangeLegacyProject()

  asyncEx {
    let! found = ops.GetMigration $"{timestamp}_legacy_case"
    Assert.True(found.IsSome)
    Assert.Equal($"{timestamp}_legacy_case", found.Value.name)
  }
  |> runAsync

[<Fact>]
let ``UpdateMigration saves and normalizes a legacy local migration``() : Task =
  let _, migrationsDir, timestamp, fileName, ops = arrangeLegacyProject()

  asyncEx {
    let! before = ops.GetMigration $"{timestamp}_legacy_case"
    Assert.True(before.IsSome)

    let edited = {
      before.Value with
          upContent = "CREATE TABLE legacy_case (id INTEGER, v TEXT);"
    }

    let! saved = ops.UpdateMigration edited
    Assert.True(Result.isOk saved)

    let reread = File.ReadAllText(Path.Combine(migrationsDir, fileName))
    Assert.Contains("-- MIGRONDI:NAME=legacy_case", reread)

    let! after = ops.GetMigration "legacy_case"
    Assert.True(after.IsSome)

    Assert.Equal(
      "CREATE TABLE legacy_case (id INTEGER, v TEXT);",
      after.Value.upContent
    )
  }
  |> runAsync

[<Fact>]
let ``DeleteMigration removes the legacy file``() : Task =
  let _, migrationsDir, timestamp, fileName, ops = arrangeLegacyProject()

  asyncEx {
    let! before = ops.GetMigration $"{timestamp}_legacy_case"
    Assert.True(before.IsSome)

    let! deleted = ops.DeleteMigration $"{timestamp}_legacy_case"
    Assert.True(Result.isOk deleted)
    Assert.False(File.Exists(Path.Combine(migrationsDir, fileName)))

    let! after = ops.GetMigration $"{timestamp}_legacy_case"
    Assert.True(after.IsNone)
  }
  |> runAsync
