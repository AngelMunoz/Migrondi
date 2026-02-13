module MigrondiUI.Tests.McpServer

open System
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

  let createTestConnectionFactory () : SqliteConnection * (unit -> IDbConnection) =
    let dbPath =
      Path.Combine(
        Path.GetTempPath(),
        $"migrondi-mcp-test-{Guid.NewGuid()}.db"
      )

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

    if not (Directory.Exists migrationsDir) then
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

  let buildTestDeps
    (connectionFactory: unit -> IDbConnection)
    (loggerFactory: ILoggerFactory)
    : IMcpWriteDeps =
    let lpr, vpr = Projects.GetRepositories connectionFactory

    let baseVirtualFactory = MigrondiExt.getMigrondiUI(loggerFactory, vpr)

    let virtualMigrondiFactory (config: MigrondiConfig, projectId: Guid) =
      baseVirtualFactory(config, "migrondi-ui://projects/virtual/", projectId)

    let localMigrondiFactory (config: MigrondiConfig, rootDir: string) =
      let mLogger = loggerFactory.CreateLogger<IMigrondi>()
      let migrondi = Migrondi.MigrondiFactory(config, rootDir, mLogger)

      { new MigrondiExt.IMigrondiUI with
          member _.DryRunDown(?amount) = migrondi.DryRunDown(?amount = amount)

          member _.DryRunDownAsync(?amount, ?cancellationToken) =
            migrondi.DryRunDownAsync(
              ?amount = amount,
              ?cancellationToken = cancellationToken
            )

          member _.DryRunUp(?amount) : IReadOnlyList<Migration> =
            migrondi.DryRunUp(?amount = amount)

          member _.DryRunUpAsync(?amount, ?cancellationToken) =
            migrondi.DryRunUpAsync(
              ?amount = amount,
              ?cancellationToken = cancellationToken
            )

          member _.Initialize() : unit = migrondi.Initialize()

          member _.InitializeAsync(?cancellationToken) : Task =
            migrondi.InitializeAsync(?cancellationToken = cancellationToken)

          member _.MigrationsList() : IReadOnlyList<MigrationStatus> =
            migrondi.MigrationsList()

          member _.MigrationsListAsync(?cancellationToken) =
            migrondi.MigrationsListAsync(?cancellationToken = cancellationToken)

          member _.RunDown(?amount) = migrondi.RunDown(?amount = amount)

          member _.RunDownAsync(?amount, ?cancellationToken) =
            migrondi.RunDownAsync(
              ?amount = amount,
              ?cancellationToken = cancellationToken
            )

          member _.RunNew(friendlyName, ?upContent, ?downContent, ?manualTransaction) =
            migrondi.RunNew(
              friendlyName,
              ?upContent = upContent,
              ?downContent = downContent,
              ?manualTransaction = manualTransaction
            )

          member _.RunNewAsync
            (
              friendlyName,
              ?upContent,
              ?downContent,
              ?manualTransaction,
              ?cancellationToken
            ) : Task<Migration> =
            migrondi.RunNewAsync(
              friendlyName,
              ?upContent = upContent,
              ?downContent = downContent,
              ?manualTransaction = manualTransaction,
              ?cancellationToken = cancellationToken
            )

          member _.RunUp(?amount) = migrondi.RunUp(?amount = amount)

          member _.RunUpAsync(?amount, ?cancellationToken) =
            migrondi.RunUpAsync(
              ?amount = amount,
              ?cancellationToken = cancellationToken
            )

          member _.RunUpdateAsync(migration: VirtualMigration, ?cancellationToken) : Task =
            vpr.UpdateMigration migration
              (defaultArg cancellationToken CancellationToken.None)

          member _.ScriptStatus(migrationPath) = migrondi.ScriptStatus(migrationPath)

          member _.ScriptStatusAsync(arg1, ?cancellationToken) =
            migrondi.ScriptStatusAsync(arg1, ?cancellationToken = cancellationToken)
      }

    let vfs =
      let logger = loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>()
      VirtualFs.getVirtualFs(logger, vpr)

    { new IMcpWriteDeps with
        member _.GetLocalProjects ct = lpr.GetProjects () ct
        member _.GetVirtualProjects ct = vpr.GetProjects () ct
        member _.GetLocalProjectById id ct = lpr.GetProjectById id ct
        member _.GetVirtualProjectById id ct = vpr.GetProjectById id ct
        member _.GetMigrationByName name ct = vpr.GetMigrationByName name ct
        member _.GetMigrations projectId ct = vpr.GetMigrations projectId ct
        member _.GetLocalMigrondi(config, rootDir) = localMigrondiFactory(config, rootDir)
        member _.GetVirtualMigrondi(config, projectId) = virtualMigrondiFactory(config, projectId)

        member _.InsertMigration migration ct = vpr.InsertMigration migration ct
        member _.UpdateMigration migration ct = vpr.UpdateMigration migration ct
        member _.RemoveMigrationByName name ct = vpr.RemoveMigrationByName name ct
        member _.InsertProject args ct = vpr.InsertProject args ct
        member _.UpdateProject project ct = vpr.UpdateProject project ct
        member _.ExportToLocal projectId path ct = vfs.ExportToLocal (projectId, path) ct
        member _.ImportFromLocal configPath ct = vfs.ImportFromLocal configPath ct
    }

type McpServerTests() =
  let masterConnection, connectionFactory = TestHelpers.createTestConnectionFactory ()
  let tempDirectory = Path.Combine(Path.GetTempPath(), $"migrondi-mcp-tests-{Guid.NewGuid()}")
  let loggerFactory = TestHelpers.createTestLoggerFactory ()
  let deps = TestHelpers.buildTestDeps connectionFactory loggerFactory

  do
    Directory.CreateDirectory tempDirectory |> ignore

  interface IDisposable with
    member _.Dispose() =
      (loggerFactory :?> IDisposable).Dispose()
      masterConnection.Dispose()

      try
        if Directory.Exists tempDirectory then
          Directory.Delete(tempDirectory, true)
      with
      | _ -> ()

  [<Fact>]
  member _.``list_projects returns empty lists initially``() = task {
    let! result = McpServerLogic.listProjects deps CancellationToken.None

    Assert.True(result.local.IsEmpty)
    Assert.True(result.virtualProjects.IsEmpty)
  }

  [<Fact>]
  member _.``create_virtual_project returns project ID``() = task {
    let! result =
      McpServerLogic.createVirtualProject
        deps
        "TestProject"
        "Data Source=:memory:"
        "sqlite"
        None
        None
        CancellationToken.None

    match result with
    | ProjectCreated p ->
      Assert.True(p.id <> Guid.Empty)
      Assert.Equal("TestProject", p.name)
      Assert.Equal("sqlite", p.driver)
    | CreateProjectError err -> Assert.Fail($"Expected success, got error: {err}")
  }

  [<Fact>]
  member _.``list_projects includes created virtual project``() = task {
    let! _ =
      McpServerLogic.createVirtualProject
        deps
        "MyProject"
        "Data Source=:memory:"
        "sqlite"
        None
        None
        CancellationToken.None

    let! result = McpServerLogic.listProjects deps CancellationToken.None

    Assert.True(result.local.IsEmpty)
    Assert.True(result.virtualProjects.Length = 1)
    Assert.Equal("MyProject", result.virtualProjects.[0].name)
  }

  [<Fact>]
  member _.``create_migration adds to virtual project``() = task {
    let! projectResult =
      McpServerLogic.createVirtualProject
        deps
        "MigrationTest"
        "Data Source=:memory:"
        "sqlite"
        None
        None
        CancellationToken.None

    let projectId =
      match projectResult with
      | ProjectCreated p -> p.id
      | CreateProjectError err -> failwith $"Failed to create project: {err}"

    let! migrationResult =
      McpServerLogic.createMigration
        deps
        (projectId.ToString())
        "add_users_table"
        (Some "CREATE TABLE users (id INTEGER PRIMARY KEY);")
        (Some "DROP TABLE users;")
        CancellationToken.None

    match migrationResult with
    | MigrationCreated m ->
      Assert.Equal("add_users_table", m.name)
      Assert.True(m.timestamp > 0L)
    | CreateMigrationError err -> Assert.Fail($"Expected success, got error: {err}")
  }

  [<Fact>]
  member _.``list_migrations for virtual project shows created migrations``() = task {
    let dbPath = Path.Combine(tempDirectory, $"virtual-test-{Guid.NewGuid()}.db")
    let connectionString = $"Data Source={dbPath}"

    let! projectResult =
      McpServerLogic.createVirtualProject
        deps
        "ListMigrationsTest"
        connectionString
        "sqlite"
        None
        None
        CancellationToken.None

    let projectId =
      match projectResult with
      | ProjectCreated p -> p.id
      | CreateProjectError err -> failwith $"Failed to create project: {err}"

    let! _ =
      McpServerLogic.createMigration
        deps
        (projectId.ToString())
        "first_migration"
        None
        None
        CancellationToken.None

    let! project = deps.GetVirtualProjectById projectId CancellationToken.None
    match project with
    | Some p ->
      let config = p.ToMigrondiConfig()
      let migrondi = deps.GetVirtualMigrondi(config, projectId)
      do! migrondi.InitializeAsync()
    | _ -> ()

    let! result = McpServerLogic.listMigrations deps projectId CancellationToken.None

    Assert.True(result.migrations.Length = 1)
    Assert.Equal("first_migration", result.migrations.[0].name)
    Assert.Equal("Pending", result.migrations.[0].status)
  }

  [<Fact>]
  member _.``get_project for non-existent ID returns not found``() = task {
    let! result =
      McpServerLogic.getProject deps (Guid.NewGuid().ToString()) CancellationToken.None

    match result with
    | ProjectNotFound msg -> Assert.True(msg.Contains("not found"))
    | _ -> Assert.Fail "Expected ProjectNotFound"
  }

  [<Fact>]
  member _.``run_migrations for non-existent project returns error``() = task {
    let! result =
      McpServerLogic.runMigrations deps (Guid.NewGuid()) None CancellationToken.None

    Assert.False result.success
    Assert.True(result.message.Contains("not found"))
  }

  [<Fact>]
  member _.``dry_run_migrations for non-existent project returns empty``() = task {
    let! result =
      McpServerLogic.dryRunMigrations deps (Guid.NewGuid()) None CancellationToken.None

    Assert.Equal(0, result.count)
    Assert.Empty result.migrations
  }

type McpLocalProjectTests() =
  let masterConnection, connectionFactory = TestHelpers.createTestConnectionFactory ()
  
  let baseUri =
    let tmp = Path.GetTempPath()
    let path = $"{Path.Combine(tmp, Guid.NewGuid().ToString())}{Path.DirectorySeparatorChar}"
    Uri(path, UriKind.Absolute)

  let rootDir = DirectoryInfo(baseUri.LocalPath)
  let loggerFactory = TestHelpers.createTestLoggerFactory ()
  let deps = TestHelpers.buildTestDeps connectionFactory loggerFactory

  do printfn $"Using '{rootDir.FullName}' as Root Directory"

  interface IDisposable with
    member _.Dispose() =
      (loggerFactory :?> IDisposable).Dispose()
      masterConnection.Dispose()

      try
        if rootDir.Exists then
          rootDir.Delete(true)
          printfn $"Deleted temporary root dir at: '{rootDir.FullName}'"
      with
      | _ -> ()

  [<Fact>]
  member _.``get_project returns local project with correct kind``() = task {
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

    use conn = connectionFactory ()
    let projectId = TestHelpers.insertLocalProject conn "LocalProject1" configPath (Some "Test description")

    let! result = McpServerLogic.getProject deps (projectId.ToString()) CancellationToken.None

    match result with
    | LocalProject p ->
      Assert.Equal("LocalProject1", p.name)
      Assert.Equal("Test description", p.description)
      Assert.Equal("local", p.kind)
      Assert.True p.config.IsSome
    | _ -> Assert.Fail $"Expected LocalProject, got {result}"
  }

  [<Fact>]
  member _.``list_migrations reads from filesystem for local project``() = task {
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

    use conn = connectionFactory ()
    let projectId = TestHelpers.insertLocalProject conn "LocalProject2" configPath None

    let! project = deps.GetLocalProjectById projectId CancellationToken.None
    match project with
    | Some p ->
      match p.config with
      | Some config ->
        let rootDir = Path.GetDirectoryName p.migrondiConfigPath |> nonNull
        let migrondi = deps.GetLocalMigrondi(config, rootDir)
        do! migrondi.InitializeAsync()
      | _ -> ()
    | _ -> ()

    let! result = McpServerLogic.listMigrations deps projectId CancellationToken.None

    Assert.True(result.migrations.Length = 1)
    Assert.Equal("create_table", result.migrations.[0].name)
    Assert.Equal("Pending", result.migrations.[0].status)
  }

  [<Fact>]
  member _.``dry_run_migrations works for local project``() = task {
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

    use conn = connectionFactory ()
    let projectId = TestHelpers.insertLocalProject conn "LocalProject3" configPath None

    let! project = deps.GetLocalProjectById projectId CancellationToken.None
    match project with
    | Some p ->
      match p.config with
      | Some config ->
        let rootDir = Path.GetDirectoryName p.migrondiConfigPath |> nonNull
        let migrondi = deps.GetLocalMigrondi(config, rootDir)
        do! migrondi.InitializeAsync()
      | _ -> ()
    | _ -> ()

    let! result =
      McpServerLogic.dryRunMigrations deps projectId (Some 1) CancellationToken.None

    Assert.True(result.count >= 1)
    Assert.True(result.migrations.Length >= 1)
  }

  [<Fact>]
  member _.``dry_run_rollback for local project with no applied migrations returns empty``() = task {
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

    Directory.CreateDirectory(Path.Combine(projectDir, "migrations")) |> ignore

    use conn = connectionFactory ()
    let projectId = TestHelpers.insertLocalProject conn "LocalProject4" configPath None

    let! project = deps.GetLocalProjectById projectId CancellationToken.None
    match project with
    | Some p ->
      match p.config with
      | Some config ->
        let rootDir = Path.GetDirectoryName p.migrondiConfigPath |> nonNull
        let migrondi = deps.GetLocalMigrondi(config, rootDir)
        do! migrondi.InitializeAsync()
      | _ -> ()
    | _ -> ()

    let! result = McpServerLogic.dryRunRollback deps projectId None CancellationToken.None

    Assert.Equal(0, result.count)
    Assert.Empty result.migrations
  }
