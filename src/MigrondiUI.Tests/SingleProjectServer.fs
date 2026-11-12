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
open Migrondi.Core.Serialization

module private Helpers =

  let createLoggerFactory() =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.None) |> ignore)

  let rawJson(result: CallToolResult) : string =
    result.StructuredContent.Value.GetRawText()

  let emptyOverrides: ProjectOverrides = {
    connection = None
    migrations = None
    tableName = None
    driver = None
  }

  /// A real temp project: migrondi.json (relative SQLite connection,
  /// anchored at the directory like the CLI writes it) and a migrations
  /// folder. Returns the directory and the config path.
  let createProjectDirectory() : string * string =
    let suffix = Guid.NewGuid().ToString("N")

    let tmp = Path.Combine(Path.GetTempPath(), $"migrondi-single-{suffix}")

    Directory.CreateDirectory(Path.Combine(tmp, "migrations")) |> ignore

    let configPath = Path.Combine(tmp, "migrondi.json")

    File.WriteAllText(
      configPath,
      """{"connection":"Data Source=migrondi.db","migrations":"migrations/","tableName":"migrations","driver":"sqlite"}"""
    )

    tmp, configPath

  /// A per-call context whose startup cwd is a real project directory.
  /// Returns the context, the project directory, and the project's config
  /// path.
  let createContext() : ToolContext * string * string * IDisposable =
    let lf = createLoggerFactory()
    let tmp, configPath = createProjectDirectory()

    let ctx: ToolContext = {
      Logger = lf.CreateLogger("Migrondi")
      Defaults = {
        Cwd = tmp
        DefaultConfigPath = None
        Overrides = emptyOverrides
      }
    }

    ctx, tmp, configPath, (lf :> IDisposable)

  let callFor(configPath: string) : ProjectCall = {
    ConfigPath = Some configPath
  }

  /// The single .sql file the create tool left in the project's migrations
  /// folder (fails the test by throwing when the folder is empty).
  let singleMigrationFile(projectDir: string) : string =
    Path.Combine(projectDir, "migrations")
    |> Directory.EnumerateFiles
    |> Seq.exactlyOne

[<Fact>]
let ``listMigrations on an empty project matches the shared encoder shape``
  ()
  : Task = task {
  let ctx, _tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let! result =
    SingleProjectTools.listMigrations
      ctx
      (Helpers.callFor configPath)
      CancellationToken.None

  let expected =
    McpResultMapper.fromEncoder
      McpResults.ListMigrationsResult.Encoder
      McpResults.ListMigrationsResult.Empty

  Assert.Equal(Helpers.rawJson expected, Helpers.rawJson result)
}

[<Fact>]
let ``createMigration without content matches the shared shape``() : Task = task {
  let ctx, tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let! result =
    SingleProjectTools.createMigration
      ctx
      (Helpers.callFor configPath)
      {
        Name = "create_users"
        Up = None
        Down = None
        ManualTransaction = None
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

  // the file landed in the TARGET project's migrations folder
  let file = Helpers.singleMigrationFile tmp

  Assert.EndsWith("create_users.sql", Path.GetFileName(file))
}

[<Fact>]
let ``createMigration with content returns the shared shape``() : Task = task {
  let ctx, _tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let! result =
    SingleProjectTools.createMigration
      ctx
      (Helpers.callFor configPath)
      {
        Name = "add_users_table"
        Up = Some "CREATE TABLE users (id INTEGER PRIMARY KEY);"
        Down = Some "DROP TABLE users;"
        ManualTransaction = None
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
    let ctx, _tmp, configPath, disposable = Helpers.createContext()

    use _ = disposable

    let! result =
      SingleProjectTools.createMigration
        ctx
        (Helpers.callFor configPath)
        {
          Name = "invalid name!"
          Up = None
          Down = None
          ManualTransaction = None
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
  let ctx, _tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let! _ =
    SingleProjectTools.createMigration
      ctx
      (Helpers.callFor configPath)
      {
        Name = "create_users"
        Up = None
        Down = None
        ManualTransaction = None
      }
      CancellationToken.None

  let! result =
    SingleProjectTools.listMigrations
      ctx
      (Helpers.callFor configPath)
      CancellationToken.None

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

  // The file name composes once: the decoded name from a standard-written
  // file already carries the timestamp, so the formula must not repeat it.
  let listedTimestamp = entry.GetProperty("timestamp").GetInt64()

  Assert.Equal(
    $"{listedTimestamp}_create_users.sql",
    entry.GetProperty("fullName").GetString()
  )
}

[<Fact>]
let ``dryRunUp previews the pending migration with the shared shape``() : Task =
  task {
    let ctx, _tmp, configPath, disposable = Helpers.createContext()

    use _ = disposable

    let! _ =
      SingleProjectTools.createMigration
        ctx
        (Helpers.callFor configPath)
        {
          Name = "create_users"
          Up = None
          Down = None
          ManualTransaction = None
        }
        CancellationToken.None

    let! result =
      SingleProjectTools.dryRunUp
        ctx
        (Helpers.callFor configPath)
        None
        CancellationToken.None

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

    let previewTimestamp = entry.GetProperty("timestamp").GetInt64()

    Assert.Equal(
      $"{previewTimestamp}_create_users.sql",
      entry.GetProperty("fullName").GetString()
    )
  }

[<Fact>]
let ``runUp applies and runDown rolls back with the shared shapes``() : Task =
  task {
    let ctx, _tmp, configPath, disposable = Helpers.createContext()

    use _ = disposable

    let! _ =
      SingleProjectTools.createMigration
        ctx
        (Helpers.callFor configPath)
        {
          Name = "create_users"
          Up = None
          Down = None
          ManualTransaction = None
        }
        CancellationToken.None

    let! runResult =
      SingleProjectTools.runUp
        ctx
        (Helpers.callFor configPath)
        None
        CancellationToken.None

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

    let appliedTimestamp = record.GetProperty("timestamp").GetInt64()

    Assert.Equal(
      $"{appliedTimestamp}_create_users.sql",
      record.GetProperty("fullName").GetString()
    )

    let! emptyRun =
      SingleProjectTools.runUp
        ctx
        (Helpers.callFor configPath)
        None
        CancellationToken.None

    Assert.False(emptyRun.IsError)

    let emptyExpected =
      McpResultMapper.fromEncoder
        McpResults.MigrationsResult.Encoder
        (McpResults.MigrationsResult.Ok(0, [||]))

    Assert.Equal(Helpers.rawJson emptyExpected, Helpers.rawJson emptyRun)

    let! downResult =
      SingleProjectTools.runDown
        ctx
        (Helpers.callFor configPath)
        None
        CancellationToken.None

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

    let rolledTimestamp = downRecord.GetProperty("timestamp").GetInt64()

    Assert.Equal(
      $"{rolledTimestamp}_create_users.sql",
      downRecord.GetProperty("fullName").GetString()
    )
  }

[<Fact>]
let ``createMigration with ManualTransaction true writes the metadata line``
  ()
  : Task = task {
  let ctx, tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let fileNamed(name: string) =
    let migrationsFolder = Path.Combine(tmp, "migrations")

    Directory.EnumerateFiles(migrationsFolder)
    |> Seq.find(fun file -> (Path.GetFileName file).EndsWith(name))

  let! _ =
    SingleProjectTools.createMigration
      ctx
      (Helpers.callFor configPath)
      {
        Name = "concurrent_index"
        Up = Some "CREATE INDEX idx ON users (id);"
        Down = Some "DROP INDEX idx;"
        ManualTransaction = Some true
      }
      CancellationToken.None

  let withFlag = fileNamed "concurrent_index.sql"

  // Core's encoder writes the bool via ToString(), so the marker is
  // "True"; the metadata line itself is what matters.
  Assert.Contains(
    "-- MIGRONDI:ManualTransaction=true",
    File.ReadAllText withFlag,
    StringComparison.OrdinalIgnoreCase
  )

  let! _ =
    SingleProjectTools.createMigration
      ctx
      (Helpers.callFor configPath)
      {
        Name = "plain_migration"
        Up = None
        Down = None
        ManualTransaction = None
      }
      CancellationToken.None

  let plainFile = fileNamed "plain_migration.sql"

  Assert.DoesNotContain(
    "-- MIGRONDI:ManualTransaction=true",
    File.ReadAllText plainFile,
    StringComparison.OrdinalIgnoreCase
  )
}

[<Fact>]
let ``initProject creates a project in an empty directory``() : Task = task {
  let ctx, _tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let target =
    Path.Combine(Path.GetTempPath(), $"migrondi-init-{Guid.NewGuid():N}")

  let! result =
    SingleProjectTools.initProject
      ctx
      {
        Directory = target
        Driver = Some "postgresql"
        Connection = Some "Host=localhost;Database=initdb"
        Migrations = Some "migrations/"
        TableName = None
        Force = None
      }
      CancellationToken.None

  Assert.False(result.IsError)

  let document = JsonDocument.Parse(Helpers.rawJson result)
  Assert.Equal("created", document.RootElement.GetProperty("mode").GetString())

  let configPath = document.RootElement.GetProperty("configPath").GetString()

  Assert.Equal(Path.Combine(target, "migrondi.json"), configPath)

  // the JSON decodes with the strict core decoder
  let config = MiSerializer.DecodeConfig(File.ReadAllText configPath)

  Assert.Equal(MigrondiDriver.Postgresql, config.driver)
  Assert.Equal("Host=localhost;Database=initdb", config.connection)
  Assert.Equal("migrations/", config.migrations)

  // the migrations directory was created (enumerating it throws when it
  // does not exist, which fails the test)
  Assert.Equal(
    0,
    Directory.EnumerateFiles(Path.Combine(target, "migrations")) |> Seq.length
  )
}

[<Fact>]
let ``initProject refuses a non-empty directory without force``() : Task = task {
  let ctx, _tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let target =
    Path.Combine(Path.GetTempPath(), $"migrondi-init-{Guid.NewGuid():N}")

  Directory.CreateDirectory target |> ignore

  File.WriteAllText(
    Path.Combine(target, "some-file.txt"),
    "not a migrondi project"
  )

  let! result =
    SingleProjectTools.initProject
      ctx
      {
        Directory = target
        Driver = None
        Connection = None
        Migrations = None
        TableName = None
        Force = None
      }
      CancellationToken.None

  Assert.True(result.IsError)
  Assert.Contains("force", Helpers.rawJson result)
}

[<Fact>]
let ``initProject adopts a non-empty directory with force, recovering fields``
  ()
  : Task = task {
  let ctx, _tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let target =
    Path.Combine(Path.GetTempPath(), $"migrondi-init-{Guid.NewGuid():N}")

  Directory.CreateDirectory(Path.Combine(target, "migrations")) |> ignore

  // a partial/invalid json: only connection and driver; the other two
  // fields must come back from the defaults
  File.WriteAllText(
    Path.Combine(target, "migrondi.json"),
    """{"connection":"Data Source=adopted.db","driver":"sqlite"}"""
  )

  let untouchedMigration =
    Path.Combine(target, "migrations", "123_adopted_me.sql")

  File.WriteAllText(untouchedMigration, "-- MIGRONDI content")

  let! result =
    SingleProjectTools.initProject
      ctx
      {
        Directory = target
        Driver = None
        Connection = None
        Migrations = None
        TableName = None
        Force = Some true
      }
      CancellationToken.None

  Assert.False(result.IsError)

  let document = JsonDocument.Parse(Helpers.rawJson result)
  Assert.Equal("adopted", document.RootElement.GetProperty("mode").GetString())

  // recovered fields: connection and driver from the file, the rest from
  // the defaults
  let config =
    MiSerializer.DecodeConfig(
      File.ReadAllText(Path.Combine(target, "migrondi.json"))
    )

  Assert.Equal("Data Source=adopted.db", config.connection)
  Assert.Equal(MigrondiDriver.Sqlite, config.driver)
  Assert.Equal("./migrations", config.migrations)
  Assert.Equal("__migrondi_migrations", config.tableName)

  // migrations untouched (reading the file throws when it is gone, which
  // fails the test)
  Assert.Equal("-- MIGRONDI content", File.ReadAllText untouchedMigration)
}

[<Fact>]
let ``updateProjectConfig round-trips the file and getProject reports it``
  ()
  : Task = task {
  let ctx, _tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable

  let call = Helpers.callFor configPath

  let! result =
    SingleProjectTools.updateProjectConfig
      ctx
      call
      {
        Connection = None
        Migrations = None
        TableName = Some "new_table"
        Driver = Some "postgresql"
      }
      CancellationToken.None

  Assert.False(result.IsError)

  let document = JsonDocument.Parse(Helpers.rawJson result)

  Assert.Equal(
    configPath,
    document.RootElement.GetProperty("configPath").GetString()
  )

  // other fields intact, changed fields written
  let config = MiSerializer.DecodeConfig(File.ReadAllText configPath)

  Assert.Equal("Data Source=migrondi.db", config.connection)
  Assert.Equal("migrations/", config.migrations)
  Assert.Equal("new_table", config.tableName)
  Assert.Equal(MigrondiDriver.Postgresql, config.driver)

  // the same per-call binding reports the new values
  let! project = SingleProjectTools.getProject ctx call CancellationToken.None

  Assert.False(project.IsError)

  let projectDocument = JsonDocument.Parse(Helpers.rawJson project)

  Assert.Equal(
    configPath,
    projectDocument.RootElement.GetProperty("configPath").GetString()
  )

  Assert.Equal(
    "new_table",
    projectDocument.RootElement.GetProperty("tableName").GetString()
  )

  Assert.Equal(
    "postgresql",
    projectDocument.RootElement.GetProperty("driver").GetString()
  )
}

[<Fact>]
let ``an explicit configPath targets that project, not the server's cwd``
  ()
  : Task = task {
  let ctx, tmp, configPath, disposable = Helpers.createContext()

  use _ = disposable
  let otherDir, otherConfig = Helpers.createProjectDirectory()

  // get_project resolves the per-call binding
  let! project =
    SingleProjectTools.getProject
      ctx
      (Helpers.callFor otherConfig)
      CancellationToken.None

  Assert.False(project.IsError)

  let document = JsonDocument.Parse(Helpers.rawJson project)

  Assert.Equal(
    otherConfig,
    document.RootElement.GetProperty("configPath").GetString()
  )

  // the reported project is B's folder, not the server's startup cwd
  let expectedName =
    Path.GetFileName(otherDir.TrimEnd(Path.DirectorySeparatorChar))

  Assert.Equal(
    expectedName,
    document.RootElement.GetProperty("name").GetString()
  )

  // the migrations path keeps the config's trailing separator
  Assert.Equal(
    Path.GetFullPath("migrations/", otherDir),
    document.RootElement.GetProperty("migrationsFolder").GetString()
  )

  // create_migration writes into the TARGET project's folder
  let! _ =
    SingleProjectTools.createMigration
      ctx
      (Helpers.callFor otherConfig)
      {
        Name = "target_b"
        Up = None
        Down = None
        ManualTransaction = None
      }
      CancellationToken.None

  let bMigrations = Path.Combine(otherDir, "migrations")
  Assert.Equal(1, Directory.EnumerateFiles(bMigrations) |> Seq.length)

  let aMigrations = Path.Combine(tmp, "migrations")
  Assert.Equal(0, Directory.EnumerateFiles(aMigrations) |> Seq.length)
}
