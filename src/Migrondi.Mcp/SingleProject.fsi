namespace Migrondi.Mcp

/// Startup-resolved defaults; every call layers its own configPath on top.
type ServerBinding = {
  /// Server startup working directory.
  Cwd: string
  /// --config, when given.
  DefaultConfigPath: string option
  /// --connection/-m/-t/-d layered over every resolved config.
  Overrides: ProjectOverrides
}

type ToolContext = {
  Logger: Microsoft.Extensions.Logging.ILogger
  Defaults: ServerBinding
}

/// Per-call routing: `Some path` targets that project; `None` falls back
/// to the startup default, then the cwd chain.
type ProjectCall = { ConfigPath: string option }

type CreateArgs = {
  Name: string
  Up: string option
  Down: string option
  ManualTransaction: bool option
}

type InitProjectArgs = {
  /// Absolute, or relative to `Defaults.Cwd`.
  Directory: string
  /// Validated via `MigrondiDriver.FromString`.
  Driver: string option
  Connection: string option
  Migrations: string option
  TableName: string option
  /// Adopt an existing non-empty directory instead of refusing it.
  Force: bool option
}

type UpdateConfigArgs = {
  Connection: string option
  Migrations: string option
  TableName: string option
  /// Validated via `MigrondiDriver.FromString`.
  Driver: string option
}

module SingleProjectTools =

  /// resolve -> factory -> Initialize -> op, for the call's own binding.
  /// Binding failures become `McpResults.ErrorResult` tool errors (message
  /// carries path/reason). Runs per call: nothing about a project is
  /// cached between calls.
  val runWithProject:
    ctx: ToolContext ->
    call: ProjectCall ->
    op:
      (Migrondi.Core.IMigrondi
        -> IcedTasks.CancellableTasks.CancellableTask<
          ModelContextProtocol.Protocol.CallToolResult
            >) ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// `MigrationsListAsync` encoded exactly like the multi-project
  /// list wrapper (`McpResults.ListMigrationsResult`).
  val listMigrations:
    ctx: ToolContext ->
    call: ProjectCall ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// `DryRunUpAsync` encoded exactly like the multi-project
  /// dry-run wrapper (`McpResults.DryRunResult`).
  val dryRunUp:
    ctx: ToolContext ->
    call: ProjectCall ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  val dryRunDown:
    ctx: ToolContext ->
    call: ProjectCall ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// `RunUpAsync` encoded exactly like the multi-project run wrapper
  /// (`McpResults.MigrationsResult`), with failures mapped to
  /// `MigrationsResult.Error` with the same messages.
  val runUp:
    ctx: ToolContext ->
    call: ProjectCall ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  val runDown:
    ctx: ToolContext ->
    call: ProjectCall ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// `RunNewAsync` encoded exactly like the multi-project create wrapper
  /// (`McpResults.CreateMigrationResult`), including the
  /// `fullName = "{timestamp}_{name}"` shape.
  val createMigration:
    ctx: ToolContext ->
    call: ProjectCall ->
    args: CreateArgs ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// Reports the resolved per-call project surface (name, config path,
  /// driver, migrations folder, table name) and NEVER the connection
  /// string.
  val getProject:
    ctx: ToolContext ->
    call: ProjectCall ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// Creates a project in the given directory (created or adopted);
  /// never deletes anything.
  val initProject:
    ctx: ToolContext ->
    args: InitProjectArgs ->
      IcedTasks.CancellableTasks.CancellableTask<
        ModelContextProtocol.Protocol.CallToolResult
       >

  /// Re-writes the targeted project's config with the given fields layered
  /// on top of its current values; never deletes anything.
  val updateProjectConfig:
    ctx: ToolContext ->
    call: ProjectCall ->
    args: UpdateConfigArgs ->
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

  /// Runs the stateless MCP server until the transport closes; every tool
  /// call resolves its own binding. `ServeArgs.ExtraTools` lets a host add
  /// binding-owned tools.
  val run:
    ctx: ToolContext ->
    loggerFactory: Microsoft.Extensions.Logging.ILoggerFactory ->
    args: ServeArgs ->
      Async<unit>
