namespace Migrondi.Mcp

type McpMode =
  | Stdio
  | Http of port: int

type McpOptions = { mode: McpMode; readOnly: bool }

/// Declares one MCP tool: identity plus the annotation matrix.
type ToolSpec = {
  Name: string
  Title: string
  ReadOnly: bool
  Destructive: bool
  Idempotent: bool
  OpenWorld: bool
}

[<NoComparison; NoEquality>]
type McpEnvironment = {
  lf: Microsoft.Extensions.Logging.ILoggerFactory
  projects: MigrondiUI.Services.IProjectCollection
  migrondiFactory: MigrondiUI.Services.IMigrationOperationsFactory
}
