namespace Migrondi.Mcp

/// The tool layer works on `IMigrondi` directly (Key Decision: it knows
/// nothing about folders or project registries).
type SingleProjectEnv = {
  lf: Microsoft.Extensions.Logging.ILoggerFactory
  migrondi: Migrondi.Core.IMigrondi
}

type CreateArgs = {
  Name: string
  Up: string option
  Down: string option
}

module SingleProjectTools =

  /// `MigrationsListAsync` encoded exactly like the multi-project
  /// list wrapper (`McpResults.ListMigrationsResult`).
  val listMigrations:
    env: SingleProjectEnv ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// `DryRunUpAsync` encoded exactly like the multi-project
  /// dry-run wrapper (`McpResults.DryRunResult`).
  val dryRunUp:
    env: SingleProjectEnv ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  val dryRunDown:
    env: SingleProjectEnv ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// `RunUpAsync` encoded exactly like the multi-project run wrapper
  /// (`McpResults.MigrationsResult`), with failures mapped to
  /// `MigrationsResult.Error` with the same messages.
  val runUp:
    env: SingleProjectEnv ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  val runDown:
    env: SingleProjectEnv ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// `RunNewAsync` encoded exactly like the multi-project create wrapper
  /// (`McpResults.CreateMigrationResult`), including the
  /// `fullName = "{timestamp}_{name}"` shape.
  val createMigration:
    env: SingleProjectEnv ->
    args: CreateArgs ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

module SingleProjectServer =

  /// Serving parameters for `run`.
  type ServeArgs = {
    Mode: McpMode
    ReadOnly: bool
    ExtraTools: ModelContextProtocol.Server.McpServerTool list
  }

  /// Runs the single-project MCP server until the transport closes;
  /// `ServeArgs.ExtraTools` lets a host add binding-owned tools
  /// (e.g. the CLI's project-info tool).
  val run:
    env: SingleProjectEnv ->
    loggerFactory: Microsoft.Extensions.Logging.ILoggerFactory ->
    args: ServeArgs ->
      Async<unit>
