namespace MigrondiUI.Mcp

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open ModelContextProtocol.Protocol

open IcedTasks

open MigrondiUI
open MigrondiUI.Projects
open Migrondi.Core

type McpMode =
  | Stdio
  | Http of port: int

type McpOptions = { mode: McpMode; readOnly: bool }

// Strongly-typed delegates for MCP tools
type ListProjectsDelegate =
  delegate of ct: CancellationToken -> Task<CallToolResult>

type GetProjectDelegate =
  delegate of projectId: string * ct: CancellationToken -> Task<CallToolResult>

type ListMigrationsDelegate =
  delegate of projectId: string * ct: CancellationToken -> Task<CallToolResult>

type GetMigrationDelegate =
  delegate of
    projectId: string * migrationName: string * ct: CancellationToken ->
      Task<CallToolResult>

type DryRunMigrationsDelegate =
  delegate of
    projectId: string * amount: int option * ct: CancellationToken ->
      Task<CallToolResult>

type RunMigrationsDelegate =
  delegate of
    projectId: string * amount: int option * ct: CancellationToken ->
      Task<CallToolResult>

type CreateMigrationDelegate =
  delegate of
    projectId: string *
    name: string *
    upContent: string option *
    downContent: string option *
    ct: CancellationToken ->
      Task<CallToolResult>

type UpdateMigrationDelegate =
  delegate of
    projectId: string *
    name: string *
    upContent: string *
    downContent: string *
    ct: CancellationToken ->
      Task<CallToolResult>

type DeleteMigrationDelegate =
  delegate of
    projectId: string * name: string * ct: CancellationToken ->
      Task<CallToolResult>

type CreateVirtualProjectDelegate =
  delegate of
    name: string *
    connection: string *
    driver: string *
    description: string option *
    tableName: string option *
    ct: CancellationToken ->
      Task<CallToolResult>

type UpdateVirtualProjectDelegate =
  delegate of
    projectId: string *
    name: string option *
    connection: string option *
    tableName: string option *
    driver: string option *
    ct: CancellationToken ->
      Task<CallToolResult>

type DeleteProjectDelegate =
  delegate of projectId: string * ct: CancellationToken -> Task<CallToolResult>

type ExportVirtualProjectDelegate =
  delegate of
    projectId: string * exportPath: string * ct: CancellationToken ->
      Task<CallToolResult>

type ImportFromLocalDelegate =
  delegate of configPath: string * ct: CancellationToken -> Task<CallToolResult>

[<NoComparison; NoEquality>]
type McpEnvironment = {
  lf: ILoggerFactory
  projects: Services.IProjectCollection
  migrondiFactory: Services.IMigrationOperationsFactory
}

module McpRuntime =

  let findProject
    (projects: Services.IProjectCollection)
    (projectId: Guid)
    : CancellableTask<Project option> =
    projects.Get(projectId)
