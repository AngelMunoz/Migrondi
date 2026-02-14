module MigrondiUI.Tests.VirtualProjectRepositoryTests

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
open Migrondi.Core

module private TestHelpers =

  let createTestConnectionFactory
    ()
    : SqliteConnection * (unit -> IDbConnection) =
    let dbPath =
      Path.Combine(Path.GetTempPath(), $"migrondi-repo-test-{Guid.NewGuid()}.db")

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

    let factory () =
      let conn = new SqliteConnection(connectionString)
      conn.Open()
      conn :> IDbConnection

    masterConnection, factory

  let createTestLoggerFactory () =
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

type VirtualProjectRepositoryTests() =
  let masterConnection, connectionFactory = TestHelpers.createTestConnectionFactory()
  let loggerFactory = TestHelpers.createTestLoggerFactory()
  let _, vProjects = Projects.GetRepositories connectionFactory

  interface IDisposable with
    member _.Dispose() =
      loggerFactory.Dispose()
      masterConnection.Dispose()

  [<Fact>]
  member _.``GetMigrationByName returns migration when it exists in specified project``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()
    let projectId = TestHelpers.insertVirtualProject conn "TestProject1" "Data Source=:memory:"

    TestHelpers.insertMigration
      conn
      projectId
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      "DROP TABLE users;"
    |> ignore

    let! result = vProjects.GetMigrationByName projectId "create_users_table" ct

    Assert.True(result.IsSome, "Migration should be found")
    Assert.Equal("create_users_table", result.Value.name)
    Assert.Equal(projectId, result.Value.projectId)
  }

  [<Fact>]
  member _.``GetMigrationByName returns None when migration exists in different project``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()
    let projectId1 = TestHelpers.insertVirtualProject conn "TestProject1" "Data Source=:memory:"
    let projectId2 = TestHelpers.insertVirtualProject conn "TestProject2" "Data Source=:memory:"

    TestHelpers.insertMigration
      conn
      projectId1
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      "DROP TABLE users;"
    |> ignore

    let! result = vProjects.GetMigrationByName projectId2 "create_users_table" ct

    Assert.True(result.IsNone, "Migration should not be found in different project")
  }

  [<Fact>]
  member _.``GetMigrationByName returns correct migration when both projects have same name``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()
    let projectId1 = TestHelpers.insertVirtualProject conn "TestProject1" "Data Source=:memory:"
    let projectId2 = TestHelpers.insertVirtualProject conn "TestProject2" "Data Source=:memory:"

    TestHelpers.insertMigration
      conn
      projectId1
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      "DROP TABLE users;"
    |> ignore

    TestHelpers.insertMigration
      conn
      projectId2
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER, name TEXT);"
      "DROP TABLE users;"
    |> ignore

    let! result1 = vProjects.GetMigrationByName projectId1 "create_users_table" ct
    let! result2 = vProjects.GetMigrationByName projectId2 "create_users_table" ct

    Assert.True(result1.IsSome, "Migration should be found in project 1")
    Assert.True(result2.IsSome, "Migration should be found in project 2")
    Assert.Equal("CREATE TABLE users (id INTEGER PRIMARY KEY);", result1.Value.upContent)
    Assert.Equal("CREATE TABLE users (id INTEGER, name TEXT);", result2.Value.upContent)
  }

  [<Fact>]
  member _.``UpdateMigration updates migration in correct project only``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()
    let projectId1 = TestHelpers.insertVirtualProject conn "TestProject1" "Data Source=:memory:"
    let projectId2 = TestHelpers.insertVirtualProject conn "TestProject2" "Data Source=:memory:"

    TestHelpers.insertMigration
      conn
      projectId1
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      "DROP TABLE users;"
    |> ignore

    TestHelpers.insertMigration
      conn
      projectId2
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER);"
      "DROP TABLE users;"
    |> ignore

    let updatedMigration: VirtualMigration = {
      id = Guid.NewGuid()
      name = "create_users_table"
      timestamp = timestamp
      upContent = "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT);"
      downContent = "DROP TABLE users;"
      projectId = projectId1
      manualTransaction = false
    }

    do! vProjects.UpdateMigration updatedMigration ct

    let! result1 = vProjects.GetMigrationByName projectId1 "create_users_table" ct
    let! result2 = vProjects.GetMigrationByName projectId2 "create_users_table" ct

    Assert.Equal("CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT);", result1.Value.upContent)
    Assert.Equal("CREATE TABLE users (id INTEGER);", result2.Value.upContent)
  }

  [<Fact>]
  member _.``RemoveMigrationByName deletes migration in correct project only``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()
    let projectId1 = TestHelpers.insertVirtualProject conn "TestProject1" "Data Source=:memory:"
    let projectId2 = TestHelpers.insertVirtualProject conn "TestProject2" "Data Source=:memory:"

    TestHelpers.insertMigration
      conn
      projectId1
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      "DROP TABLE users;"
    |> ignore

    TestHelpers.insertMigration
      conn
      projectId2
      "create_users_table"
      timestamp
      "CREATE TABLE users (id INTEGER);"
      "DROP TABLE users;"
    |> ignore

    do! vProjects.RemoveMigrationByName projectId1 "create_users_table" ct

    let! result1 = vProjects.GetMigrationByName projectId1 "create_users_table" ct
    let! result2 = vProjects.GetMigrationByName projectId2 "create_users_table" ct

    Assert.True(result1.IsNone, "Migration should be deleted from project 1")
    Assert.True(result2.IsSome, "Migration should still exist in project 2")
  }
