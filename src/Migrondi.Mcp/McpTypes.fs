namespace Migrondi.Mcp

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open ModelContextProtocol.Protocol

open IcedTasks

open MigrondiUI
open Migrondi.Core

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
  lf: ILoggerFactory
  projects: Services.IProjectCollection
  migrondiFactory: Services.IMigrationOperationsFactory
}
