module MigrondiUI.Tests.McpContractTests

open System
open System.Data
open System.IO
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks

open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open ModelContextProtocol.Protocol
open Xunit

open MigrondiUI
open MigrondiUI.Services
open Migrondi.Mcp
open Migrondi.Mcp.ServerBuilder

module private Helpers =

  let migrationName = "add_users"
  let upSql = "CREATE TABLE users (id INTEGER PRIMARY KEY);"
  let downSql = "DROP TABLE users;"

  let createLoggerFactory() =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.None) |> ignore)

  let rawJson(result: CallToolResult) : string =
    result.StructuredContent.Value.GetRawText()

  /// Recursive JSON shape: objects compare by key set (each key mapped to
  /// its child's shape), arrays compare by length with pairwise element
  /// shapes, and leaves reduce to "value". Values (ids, names, timestamps)
  /// differ between the hosts by design and are erased here.
  let rec shapeOf(node: JsonNode) : string =
    match node with
    | :? JsonObject as obj ->
      obj
      |> Seq.sortBy
        (fun (kvp: System.Collections.Generic.KeyValuePair<string, JsonNode>) ->
          kvp.Key)
      |> Seq.map(fun kvp -> $"{kvp.Key}={shapeOf kvp.Value}")
      |> String.concat ";"
      |> fun keys -> "{" + keys + "}"
    | :? JsonArray as arr ->
      arr
      |> Seq.mapi(fun (i: int) (item: JsonNode) -> $"{i}:{shapeOf item}")
      |> String.concat ";"
      |> fun items -> "[" + items + "]"
    | _ -> "value"

  let assertSameShape (cliResult: CallToolResult) (guiResult: CallToolResult) =
    Assert.False(cliResult.IsError, "the CLI host returned an error")

    Assert.False(guiResult.IsError, "the GUI host returned an error")

    let cliShape = shapeOf(JsonNode.Parse(rawJson cliResult))

    let guiShape = shapeOf(JsonNode.Parse(rawJson guiResult))

    Assert.Equal(cliShape, guiShape)

  /// The app database the multi-project environment registers projects in
  /// (same schema as McpServer.fs).
  let createTestConnectionFactory
    ()
    : SqliteConnection * (unit -> IDbConnection) =
    let dbPath =
      Path.Combine(
        Path.GetTempPath(),
        $"migrondi-contract-appdb-{Guid.NewGuid()}.db"
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

    let factory() =
      let conn = new SqliteConnection(connectionString)
      conn.Open()
      conn :> IDbConnection

    masterConnection, factory

  /// A real temp SQLite project for the CLI single-project host:
  /// migrondi.json (absolute SQLite connection) plus a migrations folder.
  let createCliProject(rootDir: string) : string * string =
    let projectDir = Path.Combine(rootDir, $"cli-{Guid.NewGuid():N}")

    Directory.CreateDirectory(Path.Combine(projectDir, "migrations")) |> ignore

    let dbPath = Path.Combine(projectDir, "migrondi.db")
    let configPath = Path.Combine(projectDir, "migrondi.json")
    let escapedConnection = dbPath.Replace("\\", "\\\\")

    File.WriteAllText(
      configPath,
      $"{{\"connection\":\"Data Source={escapedConnection}\",\"migrations\":\"migrations/\",\"tableName\":\"migrations\",\"driver\":\"sqlite\"}}"
    )

    projectDir, configPath

  /// The two hosts under contract, in equivalent state: a temp SQLite
  /// project driven through `SingleProjectTools` with a `ToolContext`
  /// bound to a temp `migrondi.json`, and a virtual project registered in
  /// the multi-project `McpEnvironment` (over a test app database, as
  /// McpServer.fs does).
  type Hosts = {
    cliCtx: ToolContext
    cliConfigPath: string
    guiEnv: McpEnvironment
    guiProjectId: Guid
    cleanup: IDisposable
  }

  let spinUp() : Task<Hosts> = task {
    let rootDir =
      Path.Combine(Path.GetTempPath(), $"migrondi-contract-{Guid.NewGuid():N}")

    Directory.CreateDirectory rootDir |> ignore

    let lf = createLoggerFactory()

    // CLI host: ToolContext bound to the temp migrondi.json
    let cliDir, cliConfigPath = createCliProject rootDir

    let cliCtx: ToolContext = {
      Logger = lf.CreateLogger "Migrondi"
      Defaults = {
        Cwd = cliDir
        DefaultConfigPath = None
        Overrides = {
          connection = None
          migrations = None
          tableName = None
          driver = None
        }
      }
    }

    // GUI host: McpEnvironment over the test app database
    let masterConnection, connectionFactory = createTestConnectionFactory()

    let env: McpEnvironment = {
      lf = lf
      projects = ProjectCollection(lf.CreateLogger(), connectionFactory)
      migrondiFactory = MigrationOperationsFactory(lf, connectionFactory)
    }

    let dbPath = Path.Combine(rootDir, $"gui-{Guid.NewGuid():N}.db")

    let newProject: Database.InsertVirtualProjectArgs = {
      name = "ContractProject"
      description = None
      connection = $"Data Source={dbPath}"
      tableName = "migrations"
      driver = "sqlite"
    }

    let! projectId =
      env.projects.CreateVirtual newProject CancellationToken.None

    // one-time target-database setup, as McpServer.fs does
    let! project = env.projects.Get projectId CancellationToken.None

    match project with
    | Some(Virtual p) ->
      let ops = env.migrondiFactory.Create(Virtual p)
      do! ops.Core.InitializeAsync CancellationToken.None
    | Some(Local _) -> Assert.Fail "Expected a virtual project"
    | None -> Assert.Fail "Expected the created project to exist"

    let cleanup = {
      new IDisposable with
        member _.Dispose() =
          lf.Dispose()
          masterConnection.Dispose()

          try
            Directory.Delete(rootDir, true)
          with _ ->
            ()
    }

    return {
      cliCtx = cliCtx
      cliConfigPath = cliConfigPath
      guiEnv = env
      guiProjectId = projectId
      cleanup = cleanup
    }
  }

  /// Creates the same named migration (with content) on both hosts; setup
  /// for the five non-create contract tests.
  let seedBoth(hosts: Hosts) : Task<unit> = task {
    let! cliSeed =
      SingleProjectTools.createMigration
        hosts.cliCtx
        {
          ConfigPath = Some hosts.cliConfigPath
        }
        {
          Name = migrationName
          Up = Some upSql
          Down = Some downSql
          ManualTransaction = None
        }
        CancellationToken.None

    let! guiSeed =
      ServerBuilder.WriteTools.createMigrationFn
        hosts.guiEnv
        (hosts.guiProjectId.ToString())
        migrationName
        (Some upSql)
        (Some downSql)
        CancellationToken.None

    Assert.False(cliSeed.IsError, "the CLI create_migration setup failed")

    Assert.False(guiSeed.IsError, "the GUI create_migration setup failed")
  }

let private cliCall(hosts: Helpers.Hosts) : ProjectCall = {
  ConfigPath = Some hosts.cliConfigPath
}

[<Fact>]
let ``create_migration with content reports the same shape on both hosts``
  ()
  : Task = task {
  let! hosts = Helpers.spinUp()
  use _ = hosts.cleanup

  let! cliResult =
    SingleProjectTools.createMigration
      hosts.cliCtx
      (cliCall hosts)
      {
        Name = Helpers.migrationName
        Up = Some Helpers.upSql
        Down = Some Helpers.downSql
        ManualTransaction = None
      }
      CancellationToken.None

  let! guiResult =
    ServerBuilder.WriteTools.createMigrationFn
      hosts.guiEnv
      (hosts.guiProjectId.ToString())
      Helpers.migrationName
      (Some Helpers.upSql)
      (Some Helpers.downSql)
      CancellationToken.None

  Helpers.assertSameShape cliResult guiResult
}

[<Fact>]
let ``list_migrations reports the same shape on both hosts``() : Task = task {
  let! hosts = Helpers.spinUp()
  use _ = hosts.cleanup
  do! Helpers.seedBoth hosts

  let! cliResult =
    SingleProjectTools.listMigrations
      hosts.cliCtx
      (cliCall hosts)
      CancellationToken.None

  let! guiResult =
    ServerBuilder.ReadTools.listMigrationsFn
      hosts.guiEnv
      (hosts.guiProjectId.ToString())
      CancellationToken.None

  Helpers.assertSameShape cliResult guiResult

  // deterministic array lengths: the one seeded migration on both sides
  let cliJson = JsonNode.Parse(Helpers.rawJson cliResult)
  let guiJson = JsonNode.Parse(Helpers.rawJson guiResult)

  Assert.Equal(1, cliJson.["migrations"].AsArray().Count)

  Assert.Equal(1, guiJson.["migrations"].AsArray().Count)
}

[<Fact>]
let ``dry_run_migrations reports the same shape on both hosts``() : Task = task {
  let! hosts = Helpers.spinUp()
  use _ = hosts.cleanup
  do! Helpers.seedBoth hosts

  let! cliResult =
    SingleProjectTools.dryRunUp
      hosts.cliCtx
      (cliCall hosts)
      None
      CancellationToken.None

  let! guiResult =
    ServerBuilder.ReadTools.dryRunMigrationsFn
      hosts.guiEnv
      (hosts.guiProjectId.ToString())
      None
      CancellationToken.None

  Helpers.assertSameShape cliResult guiResult

  // deterministic array lengths: one preview for the one pending migration
  let cliJson = JsonNode.Parse(Helpers.rawJson cliResult)

  Assert.Equal(1, cliJson.["migrations"].AsArray().Count)
}

[<Fact>]
let ``dry_run_rollback reports the same shape on both hosts``() : Task = task {
  let! hosts = Helpers.spinUp()
  use _ = hosts.cleanup
  do! Helpers.seedBoth hosts

  // nothing applied yet: both hosts report the empty deterministic shape
  let! cliResult =
    SingleProjectTools.dryRunDown
      hosts.cliCtx
      (cliCall hosts)
      None
      CancellationToken.None

  let! guiResult =
    ServerBuilder.ReadTools.dryRunRollbackFn
      hosts.guiEnv
      (hosts.guiProjectId.ToString())
      None
      CancellationToken.None

  Helpers.assertSameShape cliResult guiResult

  // deterministic array lengths: nothing applied, nothing to preview
  let cliJson = JsonNode.Parse(Helpers.rawJson cliResult)

  Assert.Equal(0, cliJson.["migrations"].AsArray().Count)
}

[<Fact>]
let ``run_migrations reports the same shape on both hosts``() : Task = task {
  let! hosts = Helpers.spinUp()
  use _ = hosts.cleanup
  do! Helpers.seedBoth hosts

  let! cliResult =
    SingleProjectTools.runUp
      hosts.cliCtx
      (cliCall hosts)
      None
      CancellationToken.None

  let! guiResult =
    ServerBuilder.WriteTools.runMigrationsFn
      hosts.guiEnv
      (hosts.guiProjectId.ToString())
      None
      CancellationToken.None

  Helpers.assertSameShape cliResult guiResult

  // deterministic array lengths: the one applied migration
  let cliJson = JsonNode.Parse(Helpers.rawJson cliResult)

  Assert.Equal(1, cliJson.["migrations"].AsArray().Count)
}

[<Fact>]
let ``run_rollback reports the same shape on both hosts``() : Task = task {
  let! hosts = Helpers.spinUp()
  use _ = hosts.cleanup
  do! Helpers.seedBoth hosts

  let! cliUp =
    SingleProjectTools.runUp
      hosts.cliCtx
      (cliCall hosts)
      None
      CancellationToken.None

  let! guiUp =
    ServerBuilder.WriteTools.runMigrationsFn
      hosts.guiEnv
      (hosts.guiProjectId.ToString())
      None
      CancellationToken.None

  Assert.False(cliUp.IsError, "the CLI run_migrations setup failed")

  Assert.False(guiUp.IsError, "the GUI run_migrations setup failed")

  let! cliResult =
    SingleProjectTools.runDown
      hosts.cliCtx
      (cliCall hosts)
      None
      CancellationToken.None

  let! guiResult =
    ServerBuilder.WriteTools.runRollbackFn
      hosts.guiEnv
      (hosts.guiProjectId.ToString())
      None
      CancellationToken.None

  Helpers.assertSameShape cliResult guiResult

  // deterministic array lengths: the one rolled-back migration
  let cliJson = JsonNode.Parse(Helpers.rawJson cliResult)

  Assert.Equal(1, cliJson.["migrations"].AsArray().Count)
}

[<Fact>]
let ``the GUI --readonly tool set is exactly the 6 read tools``() : Task = task {
  let lf = Helpers.createLoggerFactory()
  use _ = lf

  let! hosts = Helpers.spinUp()
  use _ = hosts.cleanup

  let services = ServiceCollection()
  services.AddSingleton<ILoggerFactory>(lf) |> ignore

  let serviceProvider = services.BuildServiceProvider()

  let collection =
    ServerBuilder.buildToolCollection hosts.guiEnv serviceProvider true

  let names =
    collection
    |> Seq.map(fun (tool: ModelContextProtocol.Server.McpServerTool) ->
      tool.ProtocolTool.Name)
    |> Set.ofSeq

  let expected =
    set [
      "list_projects"
      "get_project"
      "list_migrations"
      "get_migration"
      "dry_run_migrations"
      "dry_run_rollback"
    ]

  Assert.Equal<Set<string>>(expected, names)
}
