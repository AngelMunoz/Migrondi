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
open MigrondiUI.Projects
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
          description TEXT
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

    use cmd = connection.CreateCommand()

    cmd.CommandText <-
      "INSERT INTO projects (id, name, description) VALUES (@id, @name, @description)"

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
    cmd.ExecuteNonQuery() |> ignore

    use cmd2 = connection.CreateCommand()

    cmd2.CommandText <-
      "INSERT INTO virtual_projects (id, connection, table_name, driver, project_id) VALUES (@id, @connection, @table_name, @driver, @project_id)"

    let p4 = cmd2.CreateParameter()
    p4.ParameterName <- "@id"
    p4.Value <- virtualProjectId.ToString()
    cmd2.Parameters.Add(p4) |> ignore
    let p5 = cmd2.CreateParameter()
    p5.ParameterName <- "@connection"
    p5.Value <- connectionStr
    cmd2.Parameters.Add(p5) |> ignore
    let p6 = cmd2.CreateParameter()
    p6.ParameterName <- "@table_name"
    p6.Value <- "migrations"
    cmd2.Parameters.Add(p6) |> ignore
    let p7 = cmd2.CreateParameter()
    p7.ParameterName <- "@driver"
    p7.Value <- "sqlite"
    cmd2.Parameters.Add(p7) |> ignore
    let p8 = cmd2.CreateParameter()
    p8.ParameterName <- "@project_id"
    p8.Value <- baseProjectId.ToString()
    cmd2.Parameters.Add(p8) |> ignore
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

type VirtualFsTests() =
  let masterConnection, connectionFactory =
    TestHelpers.createTestConnectionFactory()

  let loggerFactory = TestHelpers.createTestLoggerFactory()
  let _, vProjects = Projects.GetRepositories connectionFactory

  let vfs =
    VirtualFs.getVirtualFs(
      loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>(),
      vProjects
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

    let! migration1 = vProjects.GetMigrationByName projectId1 "create_users" ct
    let! migration2 = vProjects.GetMigrationByName projectId2 "create_users" ct

    Assert.Equal(
      "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT);",
      migration1.Value.upContent
    )

    Assert.Equal("CREATE TABLE users (id INTEGER);", migration2.Value.upContent)
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
