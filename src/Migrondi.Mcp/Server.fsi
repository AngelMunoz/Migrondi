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
