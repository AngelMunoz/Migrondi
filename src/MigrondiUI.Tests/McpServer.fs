module MigrondiUI.Tests.McpServer

open System
open System.Data
open System.IO
open System.Threading
open System.Threading.Tasks

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open ModelContextProtocol.Protocol
open Xunit

open IcedTasks

open MigrondiUI
open MigrondiUI.Mcp
open MigrondiUI.Mcp.McpResults
open MigrondiUI.Services
open Migrondi.Core
open Migrondi.Core.Serialization

open JDeck
open System.Text.Json

module private TestHelpers =

  let createTestConnectionFactory
    ()
    : SqliteConnection * (unit -> IDbConnection) =
    let dbPath =
      Path.Combine(Path.GetTempPath(), $"migrondi-mcp-test-{Guid.NewGuid()}.db")

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

        CREATE TABLE IF NOT EXISTS local_projects (
          id TEXT PRIMARY KEY,
          config_path TEXT NOT NULL,
          project_id TEXT NOT NULL,
          FOREIGN KEY (project_id) REFERENCES projects(id)
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

  let insertLocalProject
    (connection: IDbConnection)
    (name: string)
    (configPath: string)
    (description: string option)
    =
    let projectId = Guid.NewGuid()

    use cmd = connection.CreateCommand()

    cmd.CommandText <-
      "
      INSERT INTO projects (id, name, description)
      VALUES (@id, @name, @description)"

    let p1 = cmd.CreateParameter()
    p1.ParameterName <- "@id"
    p1.Value <- projectId.ToString()
    cmd.Parameters.Add(p1) |> ignore

    let p2 = cmd.CreateParameter()
    p2.ParameterName <- "@name"
    p2.Value <- name
    cmd.Parameters.Add(p2) |> ignore

    let p3 = cmd.CreateParameter()
    p3.ParameterName <- "@description"

    p3.Value <-
      match description with
      | Some d -> box d
      | None -> DBNull.Value

    cmd.Parameters.Add(p3) |> ignore

    cmd.ExecuteNonQuery() |> ignore

    use cmd2 = connection.CreateCommand()

    cmd2.CommandText <-
      "
      INSERT INTO local_projects (id, config_path, project_id)
      VALUES (@id, @config_path, @project_id)"

    let p6 = cmd2.CreateParameter()
    p6.ParameterName <- "@id"
    p6.Value <- Guid.NewGuid().ToString()
    cmd2.Parameters.Add(p6) |> ignore

    let p7 = cmd2.CreateParameter()
    p7.ParameterName <- "@config_path"
    p7.Value <- configPath
    cmd2.Parameters.Add(p7) |> ignore

    let p8 = cmd2.CreateParameter()
    p8.ParameterName <- "@project_id"
    p8.Value <- projectId.ToString()
    cmd2.Parameters.Add(p8) |> ignore
    cmd2.ExecuteNonQuery() |> ignore

    projectId

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

  let createMigrondiJson
    (directory: string)
    (connection: string)
    (migrations: string)
    (tableName: string)
    (driver: MigrondiDriver)
    =
    let escapedConnection = connection.Replace("\\", "\\\\")

    let json =
      $@"
{{
  ""connection"": ""{escapedConnection}"",
  ""migrations"": ""{migrations}"",
  ""tableName"": ""{tableName}"",
  ""driver"": ""{driver.AsString}""
}}"

    let path = Path.Combine(directory, "migrondi.json")
    File.WriteAllText(path, json)
    path

  let createMigrationFile
    (directory: string)
    (timestamp: int64)
    (name: string)
    (upContent: string)
    (downContent: string)
    =
    let migrationsDir = Path.Combine(directory, "migrations")

    if not(Directory.Exists migrationsDir) then
      Directory.CreateDirectory migrationsDir |> ignore

    let fileName = $"{timestamp}_{name}.sql"

    let content =
      $@"-- MIGRONDI:Name={name}
-- MIGRONDI:TIMESTAMP={timestamp}
-- ---------- MIGRONDI:UP ----------
{upContent}
-- ---------- MIGRONDI:DOWN ----------
{downContent}
"

    let path = Path.Combine(migrationsDir, fileName)
    File.WriteAllText(path, content)
    path

  let buildTestEnv
    (connectionFactory: unit -> IDbConnection)
    (loggerFactory: ILoggerFactory)
    : McpEnvironment =
    let projects =
      ProjectCollection(loggerFactory.CreateLogger(), connectionFactory)

    let migrondiFactory =
      MigrationOperationsFactory(loggerFactory, connectionFactory)

    {
      lf = loggerFactory
      projects = projects
      migrondiFactory = migrondiFactory
    }

type McpServerTests() =
  let masterConnection, connectionFactory =
    TestHelpers.createTestConnectionFactory()

  let tempDirectory =
    Path.Combine(Path.GetTempPath(), $"migrondi-mcp-tests-{Guid.NewGuid()}")

  let loggerFactory = TestHelpers.createTestLoggerFactory()
  let env = TestHelpers.buildTestEnv connectionFactory loggerFactory

  do Directory.CreateDirectory tempDirectory |> ignore

  interface IDisposable with
    member _.Dispose() =
      loggerFactory.Dispose()
      masterConnection.Dispose()

      try
        if Directory.Exists tempDirectory then
          Directory.Delete(tempDirectory, true)
      with _ ->
        ()

  [<Fact>]
  member _.``list_projects returns empty lists initially``() = asyncEx {
    let! projects = env.projects.List()

    let result: ListProjectsResult = {
      local =
        projects
        |> List.choose (function
          | Local p -> Some(LocalProjectSummary.FromLocalProject p)
          | Virtual _ -> None)
      virtualProjects =
        projects
        |> List.choose (function
          | Virtual p -> Some(VirtualProjectSummary.FromVirtualProject p)
          | Local _ -> None)
    }

    Assert.True(result.local.IsEmpty)
    Assert.True(result.virtualProjects.IsEmpty)
  }

  [<Fact>]
  member _.``create_virtual_project returns project ID``() = asyncEx {
    let newProject: Database.InsertVirtualProjectArgs = {
      name = "TestProject"
      description = None
      connection = "Data Source=:memory:"
      tableName = "migrations"
      driver = "sqlite"
    }

    let! projectId = env.projects.CreateVirtual newProject

    Assert.True(projectId <> Guid.Empty)
  }

  [<Fact>]
  member _.``list_projects includes created virtual project``() = asyncEx {
    let newProject: Database.InsertVirtualProjectArgs = {
      name = "MyProject"
      description = None
      connection = "Data Source=:memory:"
      tableName = "migrations"
      driver = "sqlite"
    }

    let! _ = env.projects.CreateVirtual newProject

    let! projects = env.projects.List()

    let vProjects =
      projects
      |> List.choose (function
        | Virtual p -> Some p
        | Local _ -> None)

    Assert.True(vProjects.Length >= 1)
    Assert.Equal("MyProject", vProjects.[0].name)
  }

  [<Fact>]
  member _.``get_project for non-existent ID returns not found``() = asyncEx {
    let! result = env.projects.Get(Guid.NewGuid())

    match result with
    | None -> ()
    | Some _ -> Assert.Fail "Expected None for non-existent project"
  }

  [<Fact>]
  member _.``run_migrations for non-existent project returns error``() = asyncEx {
    let projectId = Guid.NewGuid()

    let! project = env.projects.Get projectId

    match project with
    | None ->
      let result = MigrationsResult.Error $"Project {projectId} not found"
      Assert.False result.success
      Assert.True(result.message.Contains("not found"))
    | Some _ -> Assert.Fail "Expected project not found"
  }

  [<Fact>]
  member _.``create_migration adds migration to virtual project``() = asyncEx {
    let dbPath =
      Path.Combine(tempDirectory, $"create-migration-test-{Guid.NewGuid()}.db")

    let newProject: Database.InsertVirtualProjectArgs = {
      name = "CreateMigrationTest"
      description = None
      connection = $"Data Source={dbPath}"
      tableName = "migrations"
      driver = "sqlite"
    }

    let! projectId = env.projects.CreateVirtual newProject

    let! project = env.projects.Get projectId

    match project with
    | Some(Virtual p) ->
      let ops = env.migrondiFactory.Create(Virtual p)

      let! ct = CancellableTask.getCancellationToken()
      do! ops.Core.InitializeAsync ct

      let! migration =
        ops.Core.RunNewAsync(
          "add_users_table",
          upContent = "CREATE TABLE users (id INTEGER PRIMARY KEY);",
          downContent = "DROP TABLE users;",
          cancellationToken = ct
        )

      Assert.True(migration.timestamp > 0L)
      Assert.Contains("add_users_table", migration.name)

      // Verify the migration was persisted by listing migrations
      let! migrations = ops.Core.MigrationsListAsync ct
      let result = ListMigrationsResult.FromMigrations migrations

      Assert.True(
        result.migrations.Length >= 1,
        "At least one migration should exist"
      )

      Assert.True(
        result.migrations
        |> Seq.exists(fun m -> m.name.Contains "add_users_table"),
        "The created migration should be in the list"
      )
    | Some(Local _) -> Assert.Fail "Expected Virtual project"
    | None -> Assert.Fail "Expected project to be found"
  }

  [<Fact>]
  member _.``list_migrations for virtual project shows created migrations``() = asyncEx {
    let dbPath =
      Path.Combine(tempDirectory, $"list-migrations-test-{Guid.NewGuid()}.db")

    let newProject: Database.InsertVirtualProjectArgs = {
      name = "ListMigrationsTest"
      description = None
      connection = $"Data Source={dbPath}"
      tableName = "migrations"
      driver = "sqlite"
    }

    let! projectId = env.projects.CreateVirtual newProject

    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    TestHelpers.insertMigration
      (connectionFactory())
      projectId
      "first_migration"
      timestamp
      "CREATE TABLE test (id INTEGER);"
      "DROP TABLE test;"
    |> ignore

    let! project = env.projects.Get projectId

    match project with
    | Some(Virtual p) ->
      let ops = env.migrondiFactory.Create(Virtual p)

      let! ct = CancellableTask.getCancellationToken()
      do! ops.Core.InitializeAsync ct

      let! migrations = ops.Core.MigrationsListAsync ct
      let result = ListMigrationsResult.FromMigrations migrations

      Assert.True(result.migrations.Length = 1)
      Assert.Equal("first_migration", result.migrations.[0].name)
      Assert.Equal("Pending", result.migrations.[0].status)
    | Some(Local _) -> Assert.Fail "Expected Virtual project"
    | None -> Assert.Fail "Expected project to be found"
  }

type McpLocalProjectTests() =
  let masterConnection, connectionFactory =
    TestHelpers.createTestConnectionFactory()

  let baseUri =
    let tmp = Path.GetTempPath()

    let path =
      $"{Path.Combine(tmp, Guid.NewGuid().ToString())}{Path.DirectorySeparatorChar}"

    Uri(path, UriKind.Absolute)

  let rootDir = DirectoryInfo(baseUri.LocalPath)
  let loggerFactory = TestHelpers.createTestLoggerFactory()
  let env = TestHelpers.buildTestEnv connectionFactory loggerFactory

  do printfn $"Using '{rootDir.FullName}' as Root Directory"

  interface IDisposable with
    member _.Dispose() =
      loggerFactory.Dispose()
      masterConnection.Dispose()

      try
        if rootDir.Exists then
          rootDir.Delete(true)
          printfn $"Deleted temporary root dir at: '{rootDir.FullName}'"
      with _ ->
        ()

  [<Fact>]
  member _.``get_project returns local project with correct kind``() = asyncEx {
    let projectDir = Path.Combine(rootDir.FullName, "LocalProject1")
    Directory.CreateDirectory projectDir |> ignore

    let dbPath = Path.Combine(projectDir, "test.db")

    let configPath =
      TestHelpers.createMigrondiJson
        projectDir
        $"Data Source={dbPath}"
        "migrations"
        "migrations"
        MigrondiDriver.Sqlite

    use conn = connectionFactory()

    let projectId =
      TestHelpers.insertLocalProject
        conn
        "LocalProject1"
        configPath
        (Some "Test description")

    let! result = env.projects.Get projectId

    match result with
    | Some(Local p) ->
      Assert.Equal("LocalProject1", p.name)
      Assert.Equal(Some "Test description", p.description)
      Assert.True p.config.IsSome
    | Some(Virtual _) -> Assert.Fail "Expected Local project"
    | None -> Assert.Fail "Expected project to be found"
  }

  [<Fact>]
  member _.``list_migrations reads from filesystem for local project``() = asyncEx {
    let ct = CancellationToken.None
    let projectDir = Path.Combine(rootDir.FullName, "LocalProject2")
    Directory.CreateDirectory projectDir |> ignore

    let dbPath = Path.Combine(projectDir, "test.db")

    let configPath =
      TestHelpers.createMigrondiJson
        projectDir
        $"Data Source={dbPath}"
        "migrations"
        "migrations"
        MigrondiDriver.Sqlite

    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    TestHelpers.createMigrationFile
      projectDir
      timestamp
      "create_table"
      "CREATE TABLE test (id INTEGER);"
      "DROP TABLE test;"
    |> ignore

    use conn = connectionFactory()

    let projectId =
      TestHelpers.insertLocalProject conn "LocalProject2" configPath None

    let! project = env.projects.Get projectId

    match project with
    | Some(Local p) ->
      let ops = env.migrondiFactory.Create(Local p)
      do! ops.Core.InitializeAsync ct
      let! migrations = ops.Core.MigrationsListAsync ct
      let result = ListMigrationsResult.FromMigrations migrations

      Assert.True(result.migrations.Length = 1)
      Assert.Equal("create_table", result.migrations.[0].name)
      Assert.Equal("Pending", result.migrations.[0].status)
    | Some(Virtual _) -> Assert.Fail "Expected Local project"
    | None -> Assert.Fail "Expected project to be found"
  }

  [<Fact>]
  member _.``dry_run_migrations works for local project``() = asyncEx {
    let ct = CancellationToken.None
    let projectDir = Path.Combine(rootDir.FullName, "LocalProject3")
    Directory.CreateDirectory projectDir |> ignore

    let dbPath = Path.Combine(projectDir, "test.db")

    let configPath =
      TestHelpers.createMigrondiJson
        projectDir
        $"Data Source={dbPath}"
        "migrations"
        "migrations"
        MigrondiDriver.Sqlite

    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    TestHelpers.createMigrationFile
      projectDir
      timestamp
      "add_column"
      "ALTER TABLE test ADD COLUMN name TEXT;"
      "ALTER TABLE test DROP COLUMN name;"
    |> ignore

    use conn = connectionFactory()

    let projectId =
      TestHelpers.insertLocalProject conn "LocalProject3" configPath None

    let! project = env.projects.Get projectId

    match project with
    | Some(Local p) ->
      let ops = env.migrondiFactory.Create(Local p)
      do! ops.Core.InitializeAsync ct

      let! migrations =
        ops.Core.DryRunUpAsync(amount = 1, cancellationToken = ct)

      let result = DryRunResult.FromMigrations migrations

      Assert.True(result.count >= 1)
      Assert.True(result.migrations.Length >= 1)
    | Some(Virtual _) -> Assert.Fail "Expected Local project"
    | None -> Assert.Fail "Expected project to be found"
  }

  [<Fact>]
  member _.``dry_run_rollback for local project with no applied migrations returns empty``
    ()
    =
    asyncEx {
      let ct = CancellationToken.None
      let projectDir = Path.Combine(rootDir.FullName, "LocalProject4")
      Directory.CreateDirectory projectDir |> ignore

      let dbPath = Path.Combine(projectDir, "test.db")

      let configPath =
        TestHelpers.createMigrondiJson
          projectDir
          $"Data Source={dbPath}"
          "migrations"
          "migrations"
          MigrondiDriver.Sqlite

      Directory.CreateDirectory(Path.Combine(projectDir, "migrations"))
      |> ignore

      use conn = connectionFactory()

      let projectId =
        TestHelpers.insertLocalProject conn "LocalProject4" configPath None

      let! project = env.projects.Get projectId

      match project with
      | Some(Local p) ->
        let ops = env.migrondiFactory.Create(Local p)
        do! ops.Core.InitializeAsync ct
        let! migrations = ops.Core.DryRunDownAsync(cancellationToken = ct)
        let result = DryRunResult.FromMigrations migrations

        Assert.Equal(0, result.count)
        Assert.Empty result.migrations
      | Some(Virtual _) -> Assert.Fail "Expected Local project"
      | None -> Assert.Fail "Expected project to be found"
    }

type McpToolsDomainTests() =
  let masterConnection, connectionFactory =
    TestHelpers.createTestConnectionFactory()

  let tempDirectory =
    Path.Combine(Path.GetTempPath(), $"migrondi-domain-tests-{Guid.NewGuid()}")

  let loggerFactory = TestHelpers.createTestLoggerFactory()
  let env = TestHelpers.buildTestEnv connectionFactory loggerFactory

  do Directory.CreateDirectory tempDirectory |> ignore

  interface IDisposable with
    member _.Dispose() =
      loggerFactory.Dispose()
      masterConnection.Dispose()

      try
        if Directory.Exists tempDirectory then
          Directory.Delete(tempDirectory, true)
      with _ ->
        ()

  [<Fact>]
  member _.``listProjects returns empty list initially``() = asyncEx {
    let! projects = McpTools.listProjects env CancellationToken.None

    Assert.Empty projects
  }

  [<Fact>]
  member _.``listProjects returns projects after creation``() = asyncEx {
    use conn = connectionFactory()

    let projectId =
      TestHelpers.insertVirtualProject conn "TestProject" "Data Source=:memory:"

    let! projects = McpTools.listProjects env CancellationToken.None

    Assert.Equal(1, projects.Length)

    match projects.[0] with
    | Virtual p -> Assert.Equal("TestProject", p.name)
    | Local _ -> Assert.Fail "Expected Virtual project"
  }

  [<Fact>]
  member _.``getProject returns Some for existing project``() = asyncEx {
    use conn = connectionFactory()

    let projectId =
      TestHelpers.insertVirtualProject
        conn
        "ExistingProject"
        "Data Source=:memory:"

    let! result = McpTools.getProject env projectId CancellationToken.None

    match result with
    | Some(Virtual p) -> Assert.Equal("ExistingProject", p.name)
    | Some(Local _) -> Assert.Fail "Expected Virtual project"
    | None -> Assert.Fail "Expected project to exist"
  }

  [<Fact>]
  member _.``getProject returns None for non-existent project``() = asyncEx {
    let! result =
      McpTools.getProject env (Guid.NewGuid()) CancellationToken.None

    Assert.True result.IsNone
  }

  [<Fact>]
  member _.``listMigrations returns empty list for non-existent project``() = asyncEx {
    let! migrations =
      McpTools.listMigrations env (Guid.NewGuid()) CancellationToken.None

    Assert.Empty migrations
  }

  [<Fact>]
  member _.``getMigration returns ProjectNotFound for non-existent project``() = task {
    let! result =
      McpTools.getMigration
        env
        (Guid.NewGuid())
        "test_migration"
        CancellationToken.None

    match result with
    | Error McpTools.GetMigrationError.ProjectNotFound -> ()
    | _ -> Assert.Fail "Expected ProjectNotFound error"
  }

  [<Fact>]
  member _.``getMigration returns LocalProjectsNotSupported for local project``
    ()
    =
    task {
      let ct = CancellationToken.None
      let projectDir = Path.Combine(tempDirectory, "LocalProjectGetMigration")
      Directory.CreateDirectory projectDir |> ignore

      let dbPath = Path.Combine(projectDir, "test.db")

      let configPath =
        TestHelpers.createMigrondiJson
          projectDir
          $"Data Source={dbPath}"
          "migrations"
          "migrations"
          MigrondiDriver.Sqlite

      use conn = connectionFactory()

      let projectId =
        TestHelpers.insertLocalProject
          conn
          "LocalProjectGetMigration"
          configPath
          None

      let! result = McpTools.getMigration env projectId "test" ct

      match result with
      | Error McpTools.GetMigrationError.LocalProjectsNotSupported -> ()
      | _ -> Assert.Fail "Expected LocalProjectsNotSupported error"
    }

  [<Fact>]
  member _.``createVirtualProject creates project successfully``() = asyncEx {
    let! result =
      McpTools.createVirtualProject
        env
        "NewProject"
        "Data Source=:memory:"
        MigrondiDriver.Sqlite
        (Some "Test description")
        (Some "custom_migrations")
        CancellationToken.None

    match result with
    | Ok projectId -> Assert.NotEqual(Guid.Empty, projectId)
    | Error _ -> Assert.Fail "Expected project creation to succeed"
  }

  [<Fact>]
  member _.``runMigrations returns ProjectNotFound for non-existent project``
    ()
    =
    asyncEx {
      let! result =
        McpTools.runMigrations env (Guid.NewGuid()) None CancellationToken.None

      match result with
      | Error McpTools.RunMigrationsError.ProjectNotFound -> ()
      | _ -> Assert.Fail "Expected ProjectNotFound error"
    }

  [<Fact>]
  member _.``runRollback returns ProjectNotFound for non-existent project``() = asyncEx {
    let! result =
      McpTools.runRollback env (Guid.NewGuid()) None CancellationToken.None

    match result with
    | Error McpTools.RunRollbackError.ProjectNotFound -> ()
    | _ -> Assert.Fail "Expected ProjectNotFound error"
  }

  [<Fact>]
  member _.``updateMigration returns ProjectNotFound for non-existent project``
    ()
    =
    asyncEx {
      let! result =
        McpTools.updateMigration
          env
          (Guid.NewGuid())
          "test_migration"
          "up content"
          "down content"
          CancellationToken.None

      match result with
      | Error McpTools.UpdateMigrationError.ProjectNotFound -> ()
      | _ -> Assert.Fail "Expected ProjectNotFound error"
    }

  [<Fact>]
  member _.``deleteMigration returns ProjectNotFound for non-existent project``
    ()
    =
    asyncEx {
      let! result =
        McpTools.deleteMigration
          env
          (Guid.NewGuid())
          "test_migration"
          CancellationToken.None

      match result with
      | Error McpTools.DeleteMigrationError.ProjectNotFound -> ()
      | _ -> Assert.Fail "Expected ProjectNotFound error"
    }

  [<Fact>]
  member _.``updateVirtualProject returns ProjectNotFound for non-existent project``
    ()
    =
    asyncEx {
      let! result =
        McpTools.updateVirtualProject
          env
          (Guid.NewGuid())
          (Some "NewName")
          None
          None
          None
          CancellationToken.None

      match result with
      | Error McpTools.UpdateProjectError.ProjectNotFound -> ()
      | _ -> Assert.Fail "Expected ProjectNotFound error"
    }

  [<Fact>]
  member _.``deleteProject returns ProjectNotFound for non-existent project``
    ()
    =
    asyncEx {
      let! result =
        McpTools.deleteProject env (Guid.NewGuid()) CancellationToken.None

      match result with
      | Error McpTools.DeleteProjectError.ProjectNotFound -> ()
      | _ -> Assert.Fail "Expected ProjectNotFound error"
    }

  [<Fact>]
  member _.``exportVirtualProject returns ProjectNotFound for non-existent project``
    ()
    =
    asyncEx {
      let! result =
        McpTools.exportVirtualProject
          env
          (Guid.NewGuid())
          (Path.Combine(tempDirectory, "export"))
          CancellationToken.None

      match result with
      | Error McpTools.ExportProjectError.ProjectNotFound -> ()
      | _ -> Assert.Fail "Expected ProjectNotFound error"
    }

  [<Fact>]
  member _.``importFromLocal returns error for invalid config path``() = asyncEx {
    let! result =
      McpTools.importFromLocal
        env
        "/nonexistent/path/migrondi.json"
        CancellationToken.None

    match result with
    | Error(McpTools.ImportProjectError.ImportFailed _) -> ()
    | _ -> Assert.Fail "Expected ImportFailed error"
  }

  [<Fact>]
  member _.``dryRunMigrations returns empty list for non-existent project``() = asyncEx {
    let! migrations =
      McpTools.dryRunMigrations env (Guid.NewGuid()) None CancellationToken.None

    Assert.Empty migrations
  }

  [<Fact>]
  member _.``dryRunRollback returns empty list for non-existent project``() = asyncEx {
    let! migrations =
      McpTools.dryRunRollback env (Guid.NewGuid()) None CancellationToken.None

    Assert.Empty migrations
  }

  [<Fact>]
  member _.``getMigration returns MigrationNotFound for non-existent migration``
    ()
    =
    task {
      use conn = connectionFactory()

      let projectId =
        TestHelpers.insertVirtualProject
          conn
          "ProjectForMissingMigration"
          "Data Source=:memory:"

      let! result =
        McpTools.getMigration
          env
          projectId
          "non_existent_migration"
          CancellationToken.None

      match result with
      | Error McpTools.GetMigrationError.MigrationNotFound -> ()
      | _ -> Assert.Fail "Expected MigrationNotFound error"
    }

  [<Fact>]
  member _.``getMigration returns migration details for existing migration``() = task {
    use conn = connectionFactory()

    let projectId =
      TestHelpers.insertVirtualProject
        conn
        "ProjectWithMigration"
        "Data Source=:memory:"

    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    TestHelpers.insertMigration
      conn
      projectId
      "test_migration"
      timestamp
      "CREATE TABLE test (id INTEGER);"
      "DROP TABLE test;"
    |> ignore

    let! result =
      McpTools.getMigration
        env
        projectId
        "test_migration"
        CancellationToken.None

    match result with
    | Ok m ->
      Assert.Equal("test_migration", m.name)
      Assert.Equal(timestamp, m.timestamp)
      Assert.Equal("CREATE TABLE test (id INTEGER);", m.upContent)
      Assert.Equal("DROP TABLE test;", m.downContent)
      Assert.Equal(projectId, m.projectId)
    | Error _ -> Assert.Fail "Expected migration to be found"
  }

  [<Fact>]
  member _.``createMigration returns ProjectNotFound for non-existent project``
    ()
    =
    asyncEx {
      let! result =
        McpTools.createMigration
          env
          (Guid.NewGuid())
          "test_migration"
          (Some "CREATE TABLE test (id INTEGER);")
          (Some "DROP TABLE test;")
          CancellationToken.None

      match result with
      | Error McpTools.CreateMigrationError.ProjectNotFound -> ()
      | _ -> Assert.Fail "Expected ProjectNotFound error"
    }

  [<Fact>]
  member _.``createMigration returns InvalidMigrationName for invalid name``() = asyncEx {
    use conn = connectionFactory()

    let projectId =
      TestHelpers.insertVirtualProject
        conn
        "ProjectForInvalidMigration"
        "Data Source=:memory:"

    let! result =
      McpTools.createMigration
        env
        projectId
        "invalid migration name with spaces"
        (Some "CREATE TABLE test (id INTEGER);")
        (Some "DROP TABLE test;")
        CancellationToken.None

    match result with
    | Error(McpTools.CreateMigrationError.InvalidMigrationName _) -> ()
    | _ -> Assert.Fail "Expected InvalidMigrationName error"
  }

type McpMigrationProjectScopingTests() =
  let masterConnection, connectionFactory =
    TestHelpers.createTestConnectionFactory()

  let tempDirectory =
    Path.Combine(Path.GetTempPath(), $"migrondi-scoping-tests-{Guid.NewGuid()}")

  let loggerFactory = TestHelpers.createTestLoggerFactory()
  let env = TestHelpers.buildTestEnv connectionFactory loggerFactory

  do Directory.CreateDirectory tempDirectory |> ignore

  interface IDisposable with
    member _.Dispose() =
      loggerFactory.Dispose()
      masterConnection.Dispose()

      try
        if Directory.Exists tempDirectory then
          Directory.Delete(tempDirectory, true)
      with _ ->
        ()

  [<Fact>]
  member _.``get_migration returns migration from specified project only``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()

    let dbPath1 = Path.Combine(tempDirectory, $"project1-{Guid.NewGuid()}.db")
    let dbPath2 = Path.Combine(tempDirectory, $"project2-{Guid.NewGuid()}.db")

    let projectId1 =
      TestHelpers.insertVirtualProject conn "Project1" $"Data Source={dbPath1}"

    let projectId2 =
      TestHelpers.insertVirtualProject conn "Project2" $"Data Source={dbPath2}"

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

    let! result1 = McpTools.getMigration env projectId1 "create_users" ct
    let! result2 = McpTools.getMigration env projectId2 "create_users" ct

    match result1 with
    | Ok m1 ->
      Assert.Contains(
        "CREATE TABLE users (id INTEGER PRIMARY KEY);",
        m1.upContent
      )
    | Error _ -> Assert.Fail "Expected migration from project 1"

    match result2 with
    | Ok m2 ->
      Assert.Contains(
        "CREATE TABLE users (id INTEGER, name TEXT);",
        m2.upContent
      )
    | Error _ -> Assert.Fail "Expected migration from project 2"
  }

  [<Fact>]
  member _.``get_migration returns not found when migration exists in different project``
    ()
    =
    task {
      let ct = CancellationToken.None
      let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

      use conn = connectionFactory()

      let dbPath1 = Path.Combine(tempDirectory, $"project1-{Guid.NewGuid()}.db")
      let dbPath2 = Path.Combine(tempDirectory, $"project2-{Guid.NewGuid()}.db")

      let projectId1 =
        TestHelpers.insertVirtualProject
          conn
          "Project1"
          $"Data Source={dbPath1}"

      let projectId2 =
        TestHelpers.insertVirtualProject
          conn
          "Project2"
          $"Data Source={dbPath2}"

      TestHelpers.insertMigration
        conn
        projectId1
        "create_users"
        timestamp
        "CREATE TABLE users (id INTEGER PRIMARY KEY);"
        "DROP TABLE users;"
      |> ignore

      let! result = McpTools.getMigration env projectId2 "create_users" ct

      match result with
      | Error McpTools.GetMigrationError.MigrationNotFound -> ()
      | _ -> Assert.Fail "Expected MigrationNotFound error"
    }

  [<Fact>]
  member _.``update_migration updates migration in correct project only``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()

    let dbPath1 = Path.Combine(tempDirectory, $"project1-{Guid.NewGuid()}.db")
    let dbPath2 = Path.Combine(tempDirectory, $"project2-{Guid.NewGuid()}.db")

    let projectId1 =
      TestHelpers.insertVirtualProject conn "Project1" $"Data Source={dbPath1}"

    let projectId2 =
      TestHelpers.insertVirtualProject conn "Project2" $"Data Source={dbPath2}"

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

    let! result =
      McpTools.updateMigration
        env
        projectId1
        "create_users"
        "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT);"
        "DROP TABLE users;"
        ct

    match result with
    | Ok _ -> ()
    | Error _ -> Assert.Fail "Migration update should succeed"

    let updated1 = TestHelpers.getMigrationByName conn projectId1 "create_users"
    let updated2 = TestHelpers.getMigrationByName conn projectId2 "create_users"

    Assert.Equal(
      "CREATE TABLE users (id INTEGER PRIMARY KEY, email TEXT);",
      updated1.Value.upContent
    )

    Assert.Equal("CREATE TABLE users (id INTEGER);", updated2.Value.upContent)
  }

  [<Fact>]
  member _.``delete_migration deletes migration in correct project only``() = task {
    let ct = CancellationToken.None
    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    use conn = connectionFactory()

    let dbPath1 = Path.Combine(tempDirectory, $"project1-{Guid.NewGuid()}.db")
    let dbPath2 = Path.Combine(tempDirectory, $"project2-{Guid.NewGuid()}.db")

    let projectId1 =
      TestHelpers.insertVirtualProject conn "Project1" $"Data Source={dbPath1}"

    let projectId2 =
      TestHelpers.insertVirtualProject conn "Project2" $"Data Source={dbPath2}"

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

    let! result = McpTools.deleteMigration env projectId1 "create_users" ct

    match result with
    | Ok _ -> ()
    | Error _ -> Assert.Fail "Migration deletion should succeed"

    let deleted1 = TestHelpers.getMigrationByName conn projectId1 "create_users"

    let stillExists2 =
      TestHelpers.getMigrationByName conn projectId2 "create_users"

    Assert.True(deleted1.IsNone, "Migration should be deleted from project 1")

    Assert.True(
      stillExists2.IsSome,
      "Migration should still exist in project 2"
    )
  }
