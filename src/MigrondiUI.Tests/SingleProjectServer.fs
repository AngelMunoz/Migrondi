module MigrondiUI.Tests.SingleProjectServer

open System
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Logging
open ModelContextProtocol.Protocol
open Xunit

open Migrondi.Mcp
open Migrondi.Mcp.McpResults
open Migrondi.Core

module private Helpers =

  let createLoggerFactory() =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.None) |> ignore)

  let rawJson(result: CallToolResult) : string =
    result.StructuredContent.Value.GetRawText()

  // A real IMigrondi over a temp SQLite project. `Initialize` is the
  // migrations-table setup (it delegates to database.SetupDatabase());
  // the migrations folder itself is what the CLI's `init` creates.
  let createProject() : IMigrondi =
    let suffix = Guid.NewGuid().ToString("N")

    let tmp = Path.Combine(Path.GetTempPath(), $"migrondi-single-{suffix}")

    Directory.CreateDirectory(Path.Combine(tmp, "migrations")) |> ignore

    let dbFile = Path.Combine(tmp, "migrondi.db")

    let config: MigrondiConfig = {
      connection = $"Data Source={dbFile}"
      migrations = "migrations/"
      tableName = "migrations"
      driver = MigrondiDriver.Sqlite
    }

    let migrondi = Migrondi.MigrondiFactory(config, tmp)
    migrondi.Initialize()
    migrondi

  let createEnv() : SingleProjectEnv * IDisposable =
    let lf = createLoggerFactory()
    let env = { lf = lf; migrondi = createProject() }
    env, (lf :> IDisposable)

[<Fact>]
let ``listMigrations on an empty project matches the shared encoder shape``
  ()
  : Task = task {
  let env, disposable = Helpers.createEnv()

  use _ = disposable
  let! result = SingleProjectTools.listMigrations env CancellationToken.None

  let expected =
    McpResultMapper.fromEncoder
      McpResults.ListMigrationsResult.Encoder
      McpResults.ListMigrationsResult.Empty

  Assert.Equal(Helpers.rawJson expected, Helpers.rawJson result)
}

[<Fact>]
let ``createMigration without content matches the shared shape``() : Task = task {
  let env, disposable = Helpers.createEnv()

  use _ = disposable

  let! result =
    SingleProjectTools.createMigration
      env
      {
        Name = "create_users"
        Up = None
        Down = None
      }
      CancellationToken.None

  Assert.False(result.IsError)

  // Byte parity with the shared encoder for the same data.
  let document = JsonDocument.Parse(Helpers.rawJson result)
  let migration = document.RootElement.GetProperty("migration")

  let expected =
    McpResults.CreateMigrationResult.MigrationCreated {|
      id = Guid.Parse(migration.GetProperty("id").GetString())
      name = migration.GetProperty("name").GetString()
      timestamp = migration.GetProperty("timestamp").GetInt64()
      fullName = migration.GetProperty("fullName").GetString()
    |}
    |> McpResultMapper.fromEncoder McpResults.CreateMigrationResult.Encoder

  Assert.Equal(Helpers.rawJson expected, Helpers.rawJson result)

  // the envelope reports the friendly name and the real file name
  // (single timestamp), never Core's filename-as-name return.
  Assert.Equal("create_users", migration.GetProperty("name").GetString())

  Assert.EndsWith(
    "_create_users.sql",
    migration.GetProperty("fullName").GetString()
  )
}

[<Fact>]
let ``createMigration with content returns the shared shape``() : Task = task {
  let env, disposable = Helpers.createEnv()

  use _ = disposable

  let! result =
    SingleProjectTools.createMigration
      env
      {
        Name = "add_users_table"
        Up = Some "CREATE TABLE users (id INTEGER PRIMARY KEY);"
        Down = Some "DROP TABLE users;"
      }
      CancellationToken.None

  Assert.False(result.IsError)

  let document = JsonDocument.Parse(Helpers.rawJson result)
  let migration = document.RootElement.GetProperty("migration")

  Assert.Equal("add_users_table", migration.GetProperty("name").GetString())

  Assert.EndsWith(
    "_add_users_table.sql",
    migration.GetProperty("fullName").GetString()
  )
}

[<Fact>]
let ``createMigration with an invalid name returns the error shape``() : Task =
  task {
    let env, disposable = Helpers.createEnv()

    use _ = disposable

    let! result =
      SingleProjectTools.createMigration
        env
        {
          Name = "invalid name!"
          Up = None
          Down = None
        }
        CancellationToken.None

    Assert.True(result.IsError)

    let expected =
      McpResultMapper.fromEncoder
        McpResults.CreateMigrationResult.Encoder
        (McpResults.CreateMigrationResult.CreateMigrationError "any message")

    let expectedKeys: Set<string> =
      JsonDocument.Parse(Helpers.rawJson expected).RootElement.EnumerateObject()
      |> Seq.map(fun p -> p.Name)
      |> set

    let actualKeys: Set<string> =
      JsonDocument.Parse(Helpers.rawJson result).RootElement.EnumerateObject()
      |> Seq.map(fun p -> p.Name)
      |> set

    Assert.Equal<Set<string>>(expectedKeys, actualKeys)
  }

[<Fact>]
let ``listMigrations reports the created migration with the shared shape``
  ()
  : Task = task {
  let env, disposable = Helpers.createEnv()

  use _ = disposable

  let! _ =
    SingleProjectTools.createMigration
      env
      {
        Name = "create_users"
        Up = None
        Down = None
      }
      CancellationToken.None

  let! result = SingleProjectTools.listMigrations env CancellationToken.None
  Assert.False(result.IsError)

  let document = JsonDocument.Parse(Helpers.rawJson result)
  let migrations = document.RootElement.GetProperty("migrations")
  Assert.Equal(1, migrations.GetArrayLength())

  let entry = migrations.[0]

  let expected =
    McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder {
      McpResults.ListMigrationsResult.migrations = [|
        {
          McpResults.MigrationStatusOutput.name =
            entry.GetProperty("name").GetString()
          timestamp = entry.GetProperty("timestamp").GetInt64()
          status = entry.GetProperty("status").GetString()
          fullName = entry.GetProperty("fullName").GetString()
        }
      |]
    }

  Assert.Equal(Helpers.rawJson expected, Helpers.rawJson result)
  Assert.Equal("Pending", entry.GetProperty("status").GetString())
}

[<Fact>]
let ``dryRunUp previews the pending migration with the shared shape``() : Task =
  task {
    let env, disposable = Helpers.createEnv()

    use _ = disposable

    let! _ =
      SingleProjectTools.createMigration
        env
        {
          Name = "create_users"
          Up = None
          Down = None
        }
        CancellationToken.None

    let! result = SingleProjectTools.dryRunUp env None CancellationToken.None
    Assert.False(result.IsError)

    let document = JsonDocument.Parse(Helpers.rawJson result)
    Assert.Equal(1, document.RootElement.GetProperty("count").GetInt32())

    let migrations = document.RootElement.GetProperty("migrations")
    Assert.Equal(1, migrations.GetArrayLength())

    let entry = migrations.[0]

    let expected =
      McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder {
        McpResults.DryRunResult.count = 1
        migrations = [|
          {
            McpResults.MigrationPreview.name =
              entry.GetProperty("name").GetString()
            timestamp = entry.GetProperty("timestamp").GetInt64()
            upContent = entry.GetProperty("upContent").GetString()
            downContent = entry.GetProperty("downContent").GetString()
            fullName = entry.GetProperty("fullName").GetString()
          }
        |]
      }

    Assert.Equal(Helpers.rawJson expected, Helpers.rawJson result)
  }

[<Fact>]
let ``runUp applies and runDown rolls back with the shared shapes``() : Task =
  task {
    let env, disposable = Helpers.createEnv()

    use _ = disposable

    let! _ =
      SingleProjectTools.createMigration
        env
        {
          Name = "create_users"
          Up = None
          Down = None
        }
        CancellationToken.None

    let! runResult = SingleProjectTools.runUp env None CancellationToken.None
    Assert.False(runResult.IsError)

    // Byte parity with the shared encoder for the same applied record.
    let run = JsonDocument.Parse(Helpers.rawJson runResult)
    let applied = run.RootElement.GetProperty("migrations")
    Assert.Equal(1, applied.GetArrayLength())

    let record = applied.[0]

    let expected =
      McpResults.MigrationsResult.Ok(
        1,
        [|
          {
            McpResults.AppliedMigration.name =
              record.GetProperty("name").GetString()
            timestamp = record.GetProperty("timestamp").GetInt64()
            fullName = record.GetProperty("fullName").GetString()
          }
        |]
      )
      |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder

    Assert.Equal(Helpers.rawJson expected, Helpers.rawJson runResult)

    let! emptyRun = SingleProjectTools.runUp env None CancellationToken.None
    Assert.False(emptyRun.IsError)

    let emptyExpected =
      McpResultMapper.fromEncoder
        McpResults.MigrationsResult.Encoder
        (McpResults.MigrationsResult.Ok(0, [||]))

    Assert.Equal(Helpers.rawJson emptyExpected, Helpers.rawJson emptyRun)

    let! downResult = SingleProjectTools.runDown env None CancellationToken.None
    Assert.False(downResult.IsError)

    let down = JsonDocument.Parse(Helpers.rawJson downResult)
    let downRecords = down.RootElement.GetProperty("migrations")
    Assert.Equal(1, downRecords.GetArrayLength())

    let downRecord = downRecords.[0]

    let downExpected =
      McpResults.MigrationsResult.Ok(
        1,
        [|
          {
            McpResults.AppliedMigration.name =
              downRecord.GetProperty("name").GetString()
            timestamp = downRecord.GetProperty("timestamp").GetInt64()
            fullName = downRecord.GetProperty("fullName").GetString()
          }
        |]
      )
      |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder

    Assert.Equal(Helpers.rawJson downExpected, Helpers.rawJson downResult)
  }
