namespace Migrondi.Mcp

type IMcpHttpServer =

  abstract Run:
    System.Threading.CancellationToken -> System.Threading.Tasks.Task<unit>

module HttpServer =

  /// Serves MCP over Streamable HTTP on `http://localhost:{port}/mcp/`,
  /// statelessly: one transient transport per POST, no session store,
  /// non-localhost `Origin` headers rejected.
  val runHttpServer:
    port: int ->
    serverOptions: ModelContextProtocol.Server.McpServerOptions ->
    loggerFactory: Microsoft.Extensions.Logging.ILoggerFactory ->
    serviceProvider: System.IServiceProvider ->
    ct: System.Threading.CancellationToken ->
      System.Threading.Tasks.Task<unit>

  val create:
    port: int ->
    serverOptions: ModelContextProtocol.Server.McpServerOptions ->
    loggerFactory: Microsoft.Extensions.Logging.ILoggerFactory ->
    serviceProvider: System.IServiceProvider ->
      IMcpHttpServer
