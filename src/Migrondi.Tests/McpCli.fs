namespace Migrondi.Tests.Mcp

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open System.Threading.Tasks

open Microsoft.Data.Sqlite
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.VisualStudio.TestTools.UnitTesting

open ModelContextProtocol.Client
open ModelContextProtocol.Protocol

open Migrondi.Core
open Migrondi.Env
open Migrondi.Handlers

module private McpTestHelpers =

  let emptyOverrides: McpOverrides = {
    connection = None
    migrations = None
    tableName = None
    driver = None
  }

  /// Snapshots and clears every MIGRONDI_* variable; returns a restore action.
  let clearMigrondiEnvironment() =
    let names = [
      "MIGRONDI_CONNECTION_STRING"
      "MIGRONDI_MIGRATIONS"
      "MIGRONDI_TABLE_NAME"
      "MIGRONDI_DRIVER"
    ]

    let saved =
      names
      |> List.map(fun name -> name, Environment.GetEnvironmentVariable(name))

    for name in names do
      Environment.SetEnvironmentVariable(name, null)

    fun () ->
      for (name, value) in saved do
        Environment.SetEnvironmentVariable(name, value)

  let writeConfigFile (directory: string) (content: string) =
    let configPath = Path.Combine(directory, "migrondi.json")
    File.WriteAllText(configPath, content)
    configPath

  let cliDll = Path.Combine(AppContext.BaseDirectory, "Migrondi.dll")

  /// A real temp project: migrondi.json, migrations folder, and two
  /// migration files created through the core `RunNew`.
  let createE2EProject() =
    let tmp = Path.Combine(Path.GetTempPath(), $"mcp-e2e-{Guid.NewGuid():N}")

    Directory.CreateDirectory(Path.Combine(tmp, "migrations")) |> ignore

    let configPath =
      writeConfigFile
        tmp
        """{"connection":"Data Source=migrondi.db","migrations":"migrations/","tableName":"migrations","driver":"sqlite"}"""

    let dbFile = Path.Combine(tmp, "migrondi.db")

    let config: MigrondiConfig = {
      connection = $"Data Source={dbFile}"
      migrations = "migrations/"
      tableName = "migrations"
      driver = MigrondiDriver.Sqlite
    }

    let migrondi = Migrondi.MigrondiFactory(config, tmp)
    migrondi.Initialize()

    let first =
      migrondi.RunNew(
        "e2e_first",
        upContent = "CREATE TABLE e2e (id INTEGER PRIMARY KEY);",
        downContent = "DROP TABLE e2e;"
      )

    let second =
      migrondi.RunNew(
        "e2e_second",
        upContent = "CREATE TABLE e2e2 (id INTEGER PRIMARY KEY);",
        downContent = "DROP TABLE e2e2;"
      )

    tmp, configPath, first, second

[<TestClass>]
type McpServerProcessTests() =

  let initializeLine =
    """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1.0"}}}"""

  let getProjectLine =
    """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_project","arguments":{}}}"""

  let startServer (arguments: string) (configure: ProcessStartInfo -> unit) =
    let startInfo =
      ProcessStartInfo(
        FileName = "dotnet",
        Arguments = $"\"{McpTestHelpers.cliDll}\" {arguments}",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
      )

    configure startInfo
    Process.Start(startInfo)

  [<TestMethod>]
  member _.``server rejects an explicitly disabled stdio transport``() =
    let _, configPath, _, _ = McpTestHelpers.createE2EProject()

    use serverProcess =
      startServer $"mcp --stdio false --config \"{configPath}\"" ignore

    Assert.IsTrue(serverProcess.WaitForExit(15000))
    Assert.AreNotEqual(0, serverProcess.ExitCode)

    let stderr = serverProcess.StandardError.ReadToEnd()
    StringAssert.Contains(stderr, "Only the stdio transport")

    Assert.AreEqual("", serverProcess.StandardOutput.ReadToEnd())

  [<TestMethod>]
  member _.``MIGRONDI_TABLE_NAME env var overrides the config file value``() =
    let _, configPath, _, _ = McpTestHelpers.createE2EProject()

    use serverProcess =
      startServer $"mcp --config \"{configPath}\"" (fun startInfo ->
        startInfo.EnvironmentVariables["MIGRONDI_TABLE_NAME"] <- "env_table")

    let stdin = serverProcess.StandardInput
    stdin.WriteLine initializeLine
    stdin.WriteLine getProjectLine
    stdin.Flush()

    let initializeResponse = serverProcess.StandardOutput.ReadLine()
    StringAssert.Contains(initializeResponse, "\"result\"")

    let getProjectResponse = serverProcess.StandardOutput.ReadLine()
    StringAssert.Contains(getProjectResponse, "env_table")

    stdin.Close()
    Assert.IsTrue(serverProcess.WaitForExit(15000))

  [<TestMethod>]
  member _.``server without --config serves the project in the working directory``
    ()
    =
    let tmp = Path.Combine(Path.GetTempPath(), $"mcp-cwd-{Guid.NewGuid():N}")

    Directory.CreateDirectory(Path.Combine(tmp, "migrations")) |> ignore

    McpTestHelpers.writeConfigFile
      tmp
      """{"connection":"Data Source=migrondi.db","migrations":"migrations/","tableName":"migrations","driver":"sqlite"}"""
    |> ignore

    use serverProcess =
      startServer "mcp" (fun startInfo -> startInfo.WorkingDirectory <- tmp)

    let stdin = serverProcess.StandardInput
    stdin.WriteLine initializeLine
    stdin.WriteLine getProjectLine
    stdin.Flush()

    let initializeResponse = serverProcess.StandardOutput.ReadLine()
    StringAssert.Contains(initializeResponse, "\"result\"")

    let getProjectResponse = serverProcess.StandardOutput.ReadLine()
    StringAssert.Contains(getProjectResponse, "migrondi.json")

    stdin.Close()
    Assert.IsTrue(serverProcess.WaitForExit(15000))

[<TestClass>]
type McpServerE2ETests() =

  [<TestMethod>]
  member _.``strict client completes handshake, seven tools, and the tool sequence``
    ()
    : Task = task {
    let projectDir, configPath, _, _ = McpTestHelpers.createE2EProject()

    let stderrLines = ConcurrentQueue<string>()

    let arguments =
      List<string>([ McpTestHelpers.cliDll; "mcp"; "--config"; configPath ])
      :> IList<string>

    let transportOptions =
      StdioClientTransportOptions(
        Command = "dotnet",
        Arguments = arguments,
        StandardErrorLines = Action<string>(stderrLines.Enqueue)
      )

    let transport = StdioClientTransport(transportOptions)

    let clientOptions =
      McpClientOptions(
        ClientInfo = Implementation(Name = "mstest-client", Version = "1.0.0")
      )

    // The SDK client parses stdout as JSON-RPC only: a stray log line
    // fails this handshake, so this asserts clean stdout end to end.
    let! client = McpClient.CreateAsync(transport, clientOptions)

    Assert.AreEqual("migrondi-mcp", client.ServerInfo.Name)
    Assert.AreNotEqual("1.2.0", client.ServerInfo.Version)

    let! tools = client.ListToolsAsync()

    Assert.AreEqual(7, tools.Count)

    let toolNames = tools |> Seq.map(fun tool -> tool.Name) |> Set.ofSeq

    let expectedNames =
      set [
        "list_migrations"
        "dry_run_migrations"
        "dry_run_rollback"
        "create_migration"
        "run_migrations"
        "run_rollback"
        "get_project"
      ]

    CollectionAssert.AreEquivalent(
      Array.ofSeq expectedNames,
      Array.ofSeq toolNames
    )

    // openWorldHint: false on every tool; idempotentHint: true on reads.
    for tool in tools do
      let annotations = tool.ProtocolTool.Annotations
      Assert.IsNotNull(annotations)
      Assert.IsTrue(annotations.OpenWorldHint.HasValue)
      Assert.IsFalse(annotations.OpenWorldHint.Value)

      let isReadTool =
        tool.Name = "list_migrations"
        || tool.Name = "dry_run_migrations"
        || tool.Name = "dry_run_rollback"
        || tool.Name = "get_project"

      if isReadTool then
        Assert.IsTrue(tool.ProtocolTool.Annotations.IdempotentHint.HasValue)
        Assert.IsTrue(tool.ProtocolTool.Annotations.IdempotentHint.Value)
        Assert.IsTrue(tool.ProtocolTool.Annotations.ReadOnlyHint.Value)

    let! project = client.CallToolAsync("get_project")
    Assert.IsFalse(project.IsError)

    use projectDocument =
      JsonDocument.Parse(project.StructuredContent.Value.GetRawText())

    let projectRoot = projectDocument.RootElement

    Assert.AreEqual(
      configPath,
      projectRoot.GetProperty("configPath").GetString()
    )

    Assert.AreEqual("sqlite", projectRoot.GetProperty("driver").GetString())

    Assert.AreEqual(
      "migrations",
      projectRoot.GetProperty("tableName").GetString()
    )

    Assert.IsNotNull(projectRoot.GetProperty("migrationsFolder").GetString())
    Assert.IsNotNull(projectRoot.GetProperty("name").GetString())

    let projectRaw = project.StructuredContent.Value.GetRawText()
    // NEVER the connection string.
    Assert.IsFalse(projectRaw.Contains("Data Source"))

    let! list = client.CallToolAsync("list_migrations")
    Assert.IsFalse(list.IsError)

    let listJson = list.StructuredContent.Value.GetRawText()
    StringAssert.Contains(listJson, "e2e_first")
    StringAssert.Contains(listJson, "e2e_second")
    StringAssert.Contains(listJson, "Pending")

    // The delegate binder lists every parameter as required, so agents
    // (and these calls) pass an explicit null for "all migrations".
    let allMigrations = readOnlyDict [ "amount", box null ]

    let! dryRun = client.CallToolAsync("dry_run_migrations", allMigrations)

    Assert.IsFalse(dryRun.IsError)

    let dryRunJson = dryRun.StructuredContent.Value.GetRawText()
    StringAssert.Contains(dryRunJson, "\"count\":2")

    let createArguments =
      readOnlyDict [
        "name", box "e2e_third"
        "upContent", box "CREATE TABLE e2e3 (id INTEGER PRIMARY KEY);"
        "downContent", box "DROP TABLE e2e3;"
      ]

    let! created = client.CallToolAsync("create_migration", createArguments)
    Assert.IsFalse(created.IsError)

    // the envelope reports the friendly name and the real file name with a
    // single timestamp (Core's RunNew returns the filename as `name`; the
    // envelope must not recompose the timestamp onto it).
    let createdDoc =
      JsonDocument.Parse(created.StructuredContent.Value.GetRawText())

    let createdMigration = createdDoc.RootElement.GetProperty("migration")

    Assert.AreEqual(
      "e2e_third",
      createdMigration.GetProperty("name").GetString()
    )

    let fullName = createdMigration.GetProperty("fullName").GetString()

    Assert.IsTrue(
      System.Text.RegularExpressions.Regex.IsMatch(
        fullName,
        @"^\d+_e2e_third\.sql$"
      ),
      $"fullName was '{fullName}', expected '<timestamp>_e2e_third.sql'"
    )

    let! run = client.CallToolAsync("run_migrations", allMigrations)
    Assert.IsFalse(run.IsError)

    let runJson = run.StructuredContent.Value.GetRawText()
    StringAssert.Contains(runJson, "\"count\":3")
    StringAssert.Contains(runJson, "\"success\":true")

    let! rollback = client.CallToolAsync("run_rollback", allMigrations)
    Assert.IsFalse(rollback.IsError)

    let rollbackJson = rollback.StructuredContent.Value.GetRawText()
    StringAssert.Contains(rollbackJson, "\"success\":true")

    // The config's connection string is a RELATIVE "Data Source=migrondi.db";
    // open the database the server left behind and confirm its migrations
    // table is there and empty after the rollback. This proves the server
    // anchored the database at the --config directory, not at the test's
    // working directory. No existence probe: a wrong anchor fails here as
    // "no such table".
    let dbPath = Path.Combine(projectDir, "migrondi.db")

    use connection = new SqliteConnection($"Data Source={dbPath}")

    connection.Open()

    use command = connection.CreateCommand()
    command.CommandText <- "SELECT COUNT(*) FROM migrations"
    Assert.AreEqual(0L, command.ExecuteScalar() :?> int64)

    // Dispose closes stdin: the server process must exit.
    do! client.DisposeAsync()
  }

  [<TestMethod>]
  member _.``server exits 0 after stdin closes and answers one initialize``() =
    let _, configPath, _, _ = McpTestHelpers.createE2EProject()

    let startInfo =
      ProcessStartInfo(
        FileName = "dotnet",
        Arguments = $"\"{McpTestHelpers.cliDll}\" mcp --config \"{configPath}\"",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
      )

    use serverProcess = Process.Start(startInfo)

    let stdin = serverProcess.StandardInput

    stdin.WriteLine(
      """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1.0"}}}"""
    )

    stdin.Flush()

    let response = serverProcess.StandardOutput.ReadLine()

    Assert.IsNotNull(response)
    StringAssert.Contains(response, "\"result\"")

    stdin.Close()

    Assert.IsTrue(serverProcess.WaitForExit(15000))
    Assert.AreEqual(0, serverProcess.ExitCode)

  [<TestMethod>]
  member _.``server with a missing config exits non-zero with an error on stderr``
    ()
    =
    let tmp = Path.Combine(Path.GetTempPath(), $"mcp-e2e-{Guid.NewGuid():N}")
    Directory.CreateDirectory tmp |> ignore

    let missing = Path.Combine(tmp, "no-such-config.json")

    let startInfo =
      ProcessStartInfo(
        FileName = "dotnet",
        Arguments = $"\"{McpTestHelpers.cliDll}\" mcp --config \"{missing}\"",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
      )

    use serverProcess = Process.Start(startInfo)

    Assert.IsTrue(serverProcess.WaitForExit(15000))
    Assert.AreNotEqual(0, serverProcess.ExitCode)

    let stderr = serverProcess.StandardError.ReadToEnd()
    StringAssert.Contains(stderr, "not found")

    Assert.AreEqual("", serverProcess.StandardOutput.ReadToEnd())

  [<TestMethod>]
  member _.``server rejects the http transport with a non-zero exit and clean stdout``
    ()
    =
    let _, configPath, _, _ = McpTestHelpers.createE2EProject()

    let startInfo =
      ProcessStartInfo(
        FileName = "dotnet",
        Arguments =
          $"\"{McpTestHelpers.cliDll}\" mcp --http --config \"{configPath}\"",
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
      )

    use serverProcess = Process.Start(startInfo)

    Assert.IsTrue(serverProcess.WaitForExit(15000))
    Assert.AreNotEqual(0, serverProcess.ExitCode)

    let stderr = serverProcess.StandardError.ReadToEnd()
    StringAssert.Contains(stderr, "HTTP transport is not available")

    Assert.AreEqual("", serverProcess.StandardOutput.ReadToEnd())
