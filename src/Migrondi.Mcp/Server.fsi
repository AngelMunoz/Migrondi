namespace Migrondi.Mcp

module ServerBuilder =

  /// Builds an `McpServerTool` from a delegate and its annotation spec.
  val createTool:
    serviceProvider: System.IServiceProvider ->
    spec: ToolSpec ->
    del: System.Delegate ->
      ModelContextProtocol.Server.McpServerTool

  /// Parses raw argv into `McpOptions` for a host entry point
  /// (`--stdio`, `--http <port>`, `--readonly`).
  val parseArgs: argv: string array -> McpOptions option

  /// Handlers for the multi-project read tools. Each returns the encoded
  /// `CallToolResult` served for the tool of the same name; internal so the
  /// contract tests (via `InternalsVisibleTo`) drive the exact handlers the
  /// server registers without widening the public API.
  module ReadTools =

    /// `list_migrations` handler.
    val internal listMigrationsFn:
      env: McpEnvironment ->
      pid: string ->
        IcedTasks.CancellableTasks.CancellableTask<
          ModelContextProtocol.Protocol.CallToolResult
         >

    /// `dry_run_migrations` handler.
    val internal dryRunMigrationsFn:
      env: McpEnvironment ->
      pid: string ->
      amount: int option ->
        IcedTasks.CancellableTasks.CancellableTask<
          ModelContextProtocol.Protocol.CallToolResult
         >

    /// `dry_run_rollback` handler.
    val internal dryRunRollbackFn:
      env: McpEnvironment ->
      pid: string ->
      amount: int option ->
        IcedTasks.CancellableTasks.CancellableTask<
          ModelContextProtocol.Protocol.CallToolResult
         >

  /// Handlers for the multi-project write tools; same contract as
  /// `ReadTools`.
  module WriteTools =

    /// `create_migration` handler.
    val internal createMigrationFn:
      env: McpEnvironment ->
      pid: string ->
      name: string ->
      up: string option ->
      down: string option ->
        IcedTasks.CancellableTasks.CancellableTask<
          ModelContextProtocol.Protocol.CallToolResult
         >

    /// `run_migrations` handler.
    val internal runMigrationsFn:
      env: McpEnvironment ->
      pid: string ->
      amount: int option ->
        IcedTasks.CancellableTasks.CancellableTask<
          ModelContextProtocol.Protocol.CallToolResult
         >

    /// `run_rollback` handler.
    val internal runRollbackFn:
      env: McpEnvironment ->
      pid: string ->
      amount: int option ->
        IcedTasks.CancellableTasks.CancellableTask<
          ModelContextProtocol.Protocol.CallToolResult
         >

  /// Builds the server's tool collection: the 6 read tools, plus the 10
  /// write tools when `readOnly` is false.
  val internal buildToolCollection:
    env: McpEnvironment ->
    serviceProvider: System.IServiceProvider ->
    readOnly: bool ->
      ModelContextProtocol.Server.McpServerPrimitiveCollection<
        ModelContextProtocol.Server.McpServerTool
       >

  /// Transport-running part shared by the multi-project server and the
  /// single-project server: binds the mode's transport and runs the server.
  val runTransport:
    mode: McpMode ->
    serverOptions: ModelContextProtocol.Server.McpServerOptions ->
    loggerFactory: Microsoft.Extensions.Logging.ILoggerFactory ->
    serviceProvider: System.IServiceProvider ->
      Async<unit>

  /// Runs the multi-project MCP server for a project collection.
  val run:
    env: McpEnvironment ->
    options: McpOptions ->
    loggerFactory: Microsoft.Extensions.Logging.ILoggerFactory ->
      Async<unit>
