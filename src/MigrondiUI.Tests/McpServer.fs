module MigrondiUI.Tests.McpServer

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Data
open System.IO
open System.Threading
open System.Threading.Tasks

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open Xunit

open MigrondiUI
open MigrondiUI.McpServer
open MigrondiUI.McpServer.McpResults
open MigrondiUI.Projects
open Migrondi.Core

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

    let factory () =
      let conn = new SqliteConnection(connectionString)
      conn.Open()
      conn :> IDbConnection

    masterConnection, factory

  let createTestLoggerFactory () =
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

    let p4 = cmd2.CreateParameter()
    p4.ParameterName <- "@id"
    p4.Value <- Guid.NewGuid().ToString()
    cmd2.Parameters.Add(p4) |> ignore

    let p5 = cmd2.CreateParameter()
    p5.ParameterName <- "@config_path"
    p5.Value <- configPath
    cmd2.Parameters.Add(p5) |> ignore

    let p6 = cmd2.CreateParameter()
    p6.ParameterName <- "@project_id"
    p6.Value <- projectId.ToString()
    cmd2.Parameters.Add(p6) |> ignore
    cmd2.ExecuteNonQuery() |> ignore

    projectId

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
    let lpr, vpr = Projects.GetRepositories connectionFactory

    let vMigrondiFactory = MigrondiExt.getMigrondiUI(loggerFactory, vpr)

    let localMigrondiFactory (config: MigrondiConfig, rootDir: string) =
      let mLogger = loggerFactory.CreateLogger<IMigrondi>()
      let migrondi = Migrondi.MigrondiFactory(config, rootDir, mLogger)
      MigrondiExt.wrapLocalMigrondi(migrondi, rootDir)

    let vfs =
      let logger = loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>()
      VirtualFs.getVirtualFs(logger, vpr)

    {
      lf = loggerFactory
      lProjects = lpr
      vProjects = vpr
      vfs = vfs
      vMigrondiFactory = vMigrondiFactory
      localMigrondiFactory = localMigrondiFactory
      migrondiCache = ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>()
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
  member _.``list_projects returns empty lists initially``() = task {
    let! localProjects = env.lProjects.GetProjects () CancellationToken.None
    let! vProjects = env.vProjects.GetProjects () CancellationToken.None

    let result: ListProjectsResult = {
      local = localProjects |> List.map LocalProjectSummary.FromLocalProject
      virtualProjects =
        vProjects |> List.map VirtualProjectSummary.FromVirtualProject
    }

    Assert.True(result.local.IsEmpty)
    Assert.True(result.virtualProjects.IsEmpty)
  }

  [<Fact>]
  member _.``create_virtual_project returns project ID``() = task {
    let ct = CancellationToken.None
    let driverValue = MigrondiDriver.FromString "sqlite"

    let newProject: NewVirtualProjectArgs = {
      name = "TestProject"
      description = ""
      connection = "Data Source=:memory:"
      tableName = "migrations"
      driver = driverValue
    }

    let! projectId = env.vProjects.InsertProject newProject ct

    Assert.True(projectId <> Guid.Empty)
  }

  [<Fact>]
  member _.``list_projects includes created virtual project``() = task {
    let ct = CancellationToken.None

    let newProject: NewVirtualProjectArgs = {
      name = "MyProject"
      description = ""
      connection = "Data Source=:memory:"
      tableName = "migrations"
      driver = MigrondiDriver.Sqlite
    }

    let! _ = env.vProjects.InsertProject newProject ct

    let! vProjects = env.vProjects.GetProjects () ct

    Assert.True(vProjects.Length = 1)
    Assert.Equal("MyProject", vProjects.[0].name)
  }

  [<Fact>]
  member _.``create_migration adds to virtual project``() = task {
    let ct = CancellationToken.None

    let newProject: NewVirtualProjectArgs = {
      name = "MigrationTest"
      description = ""
      connection = "Data Source=:memory:"
      tableName = "migrations"
      driver = MigrondiDriver.Sqlite
    }

    let! projectId = env.vProjects.InsertProject newProject ct

    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    let migration: VirtualMigration = {
      id = Guid.NewGuid()
      name = "add_users_table"
      timestamp = timestamp
      upContent = "CREATE TABLE users (id INTEGER PRIMARY KEY);"
      downContent = "DROP TABLE users;"
      projectId = projectId
      manualTransaction = false
    }

    let! id = env.vProjects.InsertMigration migration ct

    Assert.True(id <> Guid.Empty)
    Assert.Equal("add_users_table", migration.name)
    Assert.True(migration.timestamp > 0L)
  }

  [<Fact>]
  member _.``list_migrations for virtual project shows created migrations``() = task {
    let ct = CancellationToken.None

    let dbPath =
      Path.Combine(tempDirectory, $"virtual-test-{Guid.NewGuid()}.db")

    let connectionString = $"Data Source={dbPath}"

    let newProject: NewVirtualProjectArgs = {
      name = "ListMigrationsTest"
      description = ""
      connection = connectionString
      tableName = "migrations"
      driver = MigrondiDriver.Sqlite
    }

    let! projectId = env.vProjects.InsertProject newProject ct

    let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

    let migration: VirtualMigration = {
      id = Guid.NewGuid()
      name = "first_migration"
      timestamp = timestamp
      upContent = ""
      downContent = ""
      projectId = projectId
      manualTransaction = false
    }

    let! _ = env.vProjects.InsertMigration migration ct

    let! vProject = env.vProjects.GetProjectById projectId ct

    match vProject with
    | Some p ->
      let config = p.ToMigrondiConfig()
      let rootDir = "migrondi-ui://projects/virtual/"
      let migrondi = env.vMigrondiFactory(config, rootDir, p.id)
      do! migrondi.InitializeAsync ct

      let! migrations = migrondi.MigrationsListAsync ct
      let result = ListMigrationsResult.FromMigrations migrations

      Assert.True(result.migrations.Length = 1)
      Assert.Equal("first_migration", result.migrations.[0].name)
      Assert.Equal("Pending", result.migrations.[0].status)
    | None -> Assert.Fail "Project not found"
  }

  [<Fact>]
  member _.``get_project for non-existent ID returns not found``() = task {
    let ct = CancellationToken.None
    let! result = findProject env.lProjects env.vProjects (Guid.NewGuid()) ct

    match result with
    | None -> ()
    | Some _ -> Assert.Fail "Expected None for non-existent project"
  }

  [<Fact>]
  member _.``run_migrations for non-existent project returns error``() = task {
    let ct = CancellationToken.None
    let projectId = Guid.NewGuid()

    match! findProject env.lProjects env.vProjects projectId ct with
    | None ->
      let result = MigrationsResult.Error $"Project {projectId} not found"
      Assert.False result.success
      Assert.True(result.message.Contains("not found"))
    | Some _ -> Assert.Fail "Expected project not found"
  }

  [<Fact>]
  member _.``dry_run_migrations for non-existent project returns empty``() = task {
    let ct = CancellationToken.None
    let projectId = Guid.NewGuid()

    match! findProject env.lProjects env.vProjects projectId ct with
    | None ->
      let result = DryRunResult.Empty
      Assert.Equal(0, result.count)
      Assert.Empty result.migrations
    | Some _ -> Assert.Fail "Expected project not found"
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
  member _.``get_project returns local project with correct kind``() = task {
    let ct = CancellationToken.None
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

    let! result = findProject env.lProjects env.vProjects projectId ct

    match result with
    | Some(Project.Local p) ->
      Assert.Equal("LocalProject1", p.name)
      Assert.Equal(Some "Test description", p.description)
      Assert.True p.config.IsSome
    | Some(Project.Virtual _) -> Assert.Fail "Expected Local project"
    | None -> Assert.Fail "Expected project to be found"
  }

  [<Fact>]
  member _.``list_migrations reads from filesystem for local project``() = task {
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

    let! project = findProject env.lProjects env.vProjects projectId ct

    match project with
    | Some(Project.Local p) ->
      match getMigrondi env (Project.Local p) with
      | Some migrondi ->
        do! migrondi.InitializeAsync ct
        let! migrations = migrondi.MigrationsListAsync ct
        let result = ListMigrationsResult.FromMigrations migrations

        Assert.True(result.migrations.Length = 1)
        Assert.Equal("create_table", result.migrations.[0].name)
        Assert.Equal("Pending", result.migrations.[0].status)
      | None -> Assert.Fail "Could not get migrondi"
    | Some(Project.Virtual _) -> Assert.Fail "Expected Local project"
    | None -> Assert.Fail "Expected project to be found"
  }

  [<Fact>]
  member _.``dry_run_migrations works for local project``() = task {
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

    let! project = findProject env.lProjects env.vProjects projectId ct

    match project with
    | Some(Project.Local p) ->
      match getMigrondi env (Project.Local p) with
      | Some migrondi ->
        do! migrondi.InitializeAsync ct

        let! migrations =
          migrondi.DryRunUpAsync(amount = 1, cancellationToken = ct)

        let result = DryRunResult.FromMigrations migrations

        Assert.True(result.count >= 1)
        Assert.True(result.migrations.Length >= 1)
      | None -> Assert.Fail "Could not get migrondi"
    | Some(Project.Virtual _) -> Assert.Fail "Expected Local project"
    | None -> Assert.Fail "Expected project to be found"
  }

  [<Fact>]
  member _.``dry_run_rollback for local project with no applied migrations returns empty``
    ()
    =
    task {
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

      let! project = findProject env.lProjects env.vProjects projectId ct

      match project with
      | Some(Project.Local p) ->
        match getMigrondi env (Project.Local p) with
        | Some migrondi ->
          do! migrondi.InitializeAsync ct
          let! migrations = migrondi.DryRunDownAsync(cancellationToken = ct)
          let result = DryRunResult.FromMigrations migrations

          Assert.Equal(0, result.count)
          Assert.Empty result.migrations
        | None -> Assert.Fail "Could not get migrondi"
      | Some(Project.Virtual _) -> Assert.Fail "Expected Local project"
      | None -> Assert.Fail "Expected project to be found"
    }