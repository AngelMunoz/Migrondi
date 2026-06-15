module MigrondiUI.Tests.VirtualFsTests

open System
open System.Data
open System.IO
open System.Threading
open System.Threading.Tasks

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open Xunit

open MigrondiUI
open MigrondiUI.VirtualFs
open Migrondi.Core
open Migrondi.Core.Serialization

module private TestHelpers =

  let createTestConnectionFactory
    ()
    : SqliteConnection * (unit -> IDbConnection) =
    let dbPath =
      Path.Combine(Path.GetTempPath(), $"migrondi-vfs-test-{Guid.NewGuid()}.db")

    let connectionString = $"Data Source={dbPath}"

    let masterConnection =
      let conn = new SqliteConnection(connectionString)
      conn.Open()

      use cmd = conn.CreateCommand()

      cmd.CommandText <-
        """
        CREATE TABLE IF NOT EXISTS projects (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          description TEXT,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          deleted_at TEXT
        );

        CREATE TABLE IF NOT EXISTS virtual_projects (
          id TEXT PRIMARY KEY,
          connection TEXT NOT NULL,
          table_name TEXT NOT NULL,
          driver TEXT NOT NULL,
          project_id TEXT NOT NULL,
          FOREIGN KEY (project_id) REFERENCES projects(id)
        );

        CREATE TABLE IF NOT EXISTS virtual_migrations (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          timestamp INTEGER NOT NULL,
          up_content TEXT NOT NULL,
          down_content TEXT NOT NULL,
          manual_transaction INTEGER NOT NULL DEFAULT 0,
          virtual_project_id TEXT NOT NULL,
          FOREIGN KEY (virtual_project_id) REFERENCES virtual_projects(id)
        );
        """

      cmd.ExecuteNonQuery() |> ignore
      conn

    let factory() =
      let conn = new SqliteConnection(connectionString)
      conn.Open()
      conn :> IDbConnection

    masterConnection, factory

  let createTestLoggerFactory() =
    LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)

  let insertVirtualProject
    (connection: IDbConnection)
    (name: string)
    (connectionStr: string)
    : Guid =
    let baseProjectId = Guid.NewGuid()
    let virtualProjectId = Guid.NewGuid()
    let now = DateTime.UtcNow.ToString("o")

    use cmd = connection.CreateCommand()

    cmd.CommandText <-
      "INSERT INTO projects (id, name, description, created_at, updated_at) VALUES (@id, @name, @description, @createdAt, @updatedAt)"

    let p1 = cmd.CreateParameter()
    p1.ParameterName <- "@id"
    p1.Value <- baseProjectId.ToString()
    cmd.Parameters.Add(p1) |> ignore
    let p2 = cmd.CreateParameter()
    p2.ParameterName <- "@name"
    p2.Value <- name
    cmd.Parameters.Add(p2) |> ignore
    let p3 = cmd.CreateParameter()
    p3.ParameterName <- "@description"
    p3.Value <- DBNull.Value
    cmd.Parameters.Add(p3) |> ignore
    let p4 = cmd.CreateParameter()
    p4.ParameterName <- "@createdAt"
    p4.Value <- now
    cmd.Parameters.Add(p4) |> ignore
    let p5 = cmd.CreateParameter()
    p5.ParameterName <- "@updatedAt"
    p5.Value <- now
    cmd.Parameters.Add(p5) |> ignore
    cmd.ExecuteNonQuery() |> ignore

    use cmd2 = connection.CreateCommand()

    cmd2.CommandText <-
      "INSERT INTO virtual_projects (id, connection, table_name, driver, project_id) VALUES (@id, @connection, @table_name, @driver, @project_id)"

    let p6 = cmd2.CreateParameter()
    p6.ParameterName <- "@id"
    p6.Value <- virtualProjectId.ToString()
    cmd2.Parameters.Add(p6) |> ignore
    let p7 = cmd2.CreateParameter()
    p7.ParameterName <- "@connection"
    p7.Value <- connectionStr
    cmd2.Parameters.Add(p7) |> ignore
    let p8 = cmd2.CreateParameter()
    p8.ParameterName <- "@table_name"
    p8.Value <- "migrations"
    cmd2.Parameters.Add(p8) |> ignore
    let p9 = cmd2.CreateParameter()
    p9.ParameterName <- "@driver"
    p9.Value <- "sqlite"
    cmd2.Parameters.Add(p9) |> ignore
    let p10 = cmd2.CreateParameter()
    p10.ParameterName <- "@project_id"
    p10.Value <- baseProjectId.ToString()
    cmd2.Parameters.Add(p10) |> ignore
    cmd2.ExecuteNonQuery() |> ignore

    virtualProjectId

  let insertMigration
    (connection: IDbConnection)
    (projectId: Guid)
    (name: string)
    (timestamp: int64)
    (upContent: string)
    (downContent: string)
    : Guid =
    let migrationId = Guid.NewGuid()

    use cmd = connection.CreateCommand()

    cmd.CommandText <-
      "INSERT INTO virtual_migrations (id, name, timestamp, up_content, down_content, virtual_project_id, manual_transaction) VALUES (@id, @name, @timestamp, @up_content, @down_content, @virtual_project_id, @manual_transaction)"

    let p1 = cmd.CreateParameter()
    p1.ParameterName <- "@id"
    p1.Value <- migrationId.ToString()
    cmd.Parameters.Add(p1) |> ignore
    let p2 = cmd.CreateParameter()
    p2.ParameterName <- "@name"
    p2.Value <- name
    cmd.Parameters.Add(p2) |> ignore
    let p3 = cmd.CreateParameter()
    p3.ParameterName <- "@timestamp"
    p3.Value <- timestamp
    cmd.Parameters.Add(p3) |> ignore
    let p4 = cmd.CreateParameter()
    p4.ParameterName <- "@up_content"
    p4.Value <- upContent
    cmd.Parameters.Add(p4) |> ignore
    let p5 = cmd.CreateParameter()
    p5.ParameterName <- "@down_content"
    p5.Value <- downContent
    cmd.Parameters.Add(p5) |> ignore
    let p6 = cmd.CreateParameter()
    p6.ParameterName <- "@virtual_project_id"
    p6.Value <- projectId.ToString()
    cmd.Parameters.Add(p6) |> ignore
    let p7 = cmd.CreateParameter()
    p7.ParameterName <- "@manual_transaction"
    p7.Value <- 0
    cmd.Parameters.Add(p7) |> ignore
    cmd.ExecuteNonQuery() |> ignore

    migrationId

  let getMigrationByName
    (connection: IDbConnection)
    (projectId: Guid)
    (name: string)
    : VirtualMigration option =
    use cmd = connection.CreateCommand()

    cmd.CommandText <-
      "
      SELECT vm.id, vm.name, vm.timestamp, vm.up_content, vm.down_content, vm.manual_transaction, vm.virtual_project_id
      FROM virtual_migrations vm
      WHERE vm.virtual_project_id = @projectId AND vm.name = @name
      "

    let p1 = cmd.CreateParameter()
    p1.ParameterName <- "@projectId"
    p1.Value <- projectId.ToString()
    cmd.Parameters.Add(p1) |> ignore
    let p2 = cmd.CreateParameter()
    p2.ParameterName <- "@name"
    p2.Value <- name
    cmd.Parameters.Add(p2) |> ignore

    use reader = cmd.ExecuteReader()

    if reader.Read() then
      Some {
        id = reader.GetString(0) |> Guid.Parse
        name = reader.GetString(1)
        timestamp = reader.GetInt64(2)
        upContent = reader.GetString(3)
        downContent = reader.GetString(4)
        manualTransaction = reader.GetInt32(5) <> 0
        projectId = reader.GetString(6) |> Guid.Parse
      }
    else
      None

  let getMigrations
    (connection: IDbConnection)
    (projectId: Guid)
    : VirtualMigration list =
    use cmd = connection.CreateCommand()

    cmd.CommandText <-
      "
      SELECT vm.id, vm.name, vm.timestamp, vm.up_content, vm.down_content, vm.manual_transaction, vm.virtual_project_id
      FROM virtual_migrations vm
      WHERE vm.virtual_project_id = @projectId
      ORDER BY vm.timestamp
      "

    let p1 = cmd.CreateParameter()
    p1.ParameterName <- "@projectId"
    p1.Value <- projectId.ToString()
    cmd.Parameters.Add(p1) |> ignore

    use reader = cmd.ExecuteReader()

    [
      while reader.Read() do
        {
          id = reader.GetString(0) |> Guid.Parse
          name = reader.GetString(1)
          timestamp = reader.GetInt64(2)
          upContent = reader.GetString(3)
          downContent = reader.GetString(4)
          manualTransaction = reader.GetInt32(5) <> 0
          projectId = reader.GetString(6) |> Guid.Parse
        }
    ]

type VirtualFsTests() =
  let masterConnection, connectionFactory =
    TestHelpers.createTestConnectionFactory()

  let loggerFactory = TestHelpers.createTestLoggerFactory()

  let vfs =
    VirtualFs.getVirtualFs(
      loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>(),
      connectionFactory
    )

  interface IDisposable with
    member _.Dispose() =
      loggerFactory.Dispose()
      masterConnection.Dispose()

  [<Fact>]
  member _.``ReadContentAsync reads migration from correct project only``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()

    let projectId1 =
      TestHelpers.insertVirtualProject conn "Project1" "Data Source=:memory:"

    let projectId2 =
      TestHelpers.insertVirtualProject conn "Project2" "Data Source=:memory:"

    TestHelpers.insertMigration
      conn
      projectId1
      "create_users"
      timestamp
      "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      "DROP TABLE users;"
    |> ignore

    TestHelpers.insertMigration
      conn
      projectId2
      "create_users"
      timestamp
      "CREATE TABLE users (id INTEGER, name TEXT);"
      "DROP TABLE users;"
    |> ignore

    let uri1 =
      Uri(
        $"migrondi-ui://projects/virtual/{projectId1}/{timestamp}_create_users.sql"
      )

    let uri2 =
      Uri(
        $"migrondi-ui://projects/virtual/{projectId2}/{timestamp}_create_users.sql"
      )

    let! content1 = vfs.ReadContentAsync(uri1, ct)
    let! content2 = vfs.ReadContentAsync(uri2, ct)

    Assert.Contains("CREATE TABLE users (id INTEGER PRIMARY KEY);", content1)
    Assert.Contains("CREATE TABLE users (id INTEGER, name TEXT);", content2)
  }

  [<Fact>]
  member _.``WriteContentAsync updates migration in correct project only``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()

    let projectId1 =
      TestHelpers.insertVirtualProject conn "Project1" "Data Source=:memory:"

    let projectId2 =
      TestHelpers.insertVirtualProject conn "Project2" "Data Source=:memory:"

    TestHelpers.insertMigration
      conn
      projectId1
      "create_users"
      timestamp
      "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      "DROP TABLE users;"
    |> ignore

    TestHelpers.insertMigration
      conn
      projectId2
      "create_users"
      timestamp
      "CREATE TABLE users (id INTEGER);"
      "DROP TABLE users;"
    |> ignore

    let uri1 =
      Uri(
        $"migrondi-ui://projects/virtual/{projectId1}/{timestamp}_create_users.sql"
      )

    let updatedContent =
      MiSerializer.Encode {
        name = "create_users"
        timestamp = timestamp
        upContent = "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT);"
        downContent = "DROP TABLE users;"
        manualTransaction = false
      }

    do! vfs.WriteContentAsync(uri1, updatedContent, ct)

    let migration1 = TestHelpers.getMigrationByName conn projectId1 "create_users"
    let migration2 = TestHelpers.getMigrationByName conn projectId2 "create_users"

    Assert.Equal(
      "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT);",
      migration1.Value.upContent
    )

    Assert.Equal("CREATE TABLE users (id INTEGER);", migration2.Value.upContent)
  }

  [<Fact>]
  member _.``WriteContentAsync updates existing migration instead of inserting duplicate``
    ()
    =
    task {
      let ct = CancellationToken.None
      let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

      use conn = connectionFactory()

      let projectId =
        TestHelpers.insertVirtualProject conn "UpdateTest" "Data Source=:memory:"

      TestHelpers.insertMigration
        conn
        projectId
        "update_test"
        timestamp
        "CREATE TABLE users (id INTEGER PRIMARY KEY);"
        "DROP TABLE users;"
      |> ignore

      let migrationUri =
        Uri(
          $"migrondi-ui://projects/virtual/{projectId}/{timestamp}_update_test.sql"
        )

      let updatedContent =
        MiSerializer.Encode {
          name = "update_test"
          timestamp = timestamp
          upContent = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);"
          downContent = "DROP TABLE users;"
          manualTransaction = false
        }

      do! vfs.WriteContentAsync(migrationUri, updatedContent, ct)

      let migrations = TestHelpers.getMigrations conn projectId

      Assert.True(
        migrations.Length = 1,
        $"Expected 1 migration after update, but found {migrations.Length}. This indicates WriteContentAsync inserted a duplicate instead of updating."
      )

      let updatedMigration =
        TestHelpers.getMigrationByName conn projectId "update_test"

      match updatedMigration with
      | Some m ->
        Assert.Equal(
          "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT);",
          m.upContent
        )
      | None -> Assert.Fail "Migration should exist after update"
    }

  [<Fact>]
  member _.``ReadContentAsync returns not found when migration exists in different project``
    ()
    =
    task {
      let ct = CancellationToken.None
      let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

      use conn = connectionFactory()

      let projectId1 =
        TestHelpers.insertVirtualProject conn "Project1" "Data Source=:memory:"

      let projectId2 =
        TestHelpers.insertVirtualProject conn "Project2" "Data Source=:memory:"

      TestHelpers.insertMigration
        conn
        projectId1
        "create_users"
        timestamp
        "CREATE TABLE users (id INTEGER PRIMARY KEY);"
        "DROP TABLE users;"
      |> ignore

      let uri2 =
        Uri(
          $"migrondi-ui://projects/virtual/{projectId2}/{timestamp}_create_users.sql"
        )

      let! ex =
        Assert.ThrowsAsync<Exception>(fun () ->
          vfs.ReadContentAsync(uri2, ct) :> Task)

      Assert.Contains("not found", ex.Message.ToLower())
    }
