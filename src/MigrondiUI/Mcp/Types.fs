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
  lProjects: ILocalProjectRepository
  vProjects: IVirtualProjectRepository
  vfs: VirtualFs.MigrondiUIFs
  vMigrondiFactory: MigrondiConfig * string * Guid -> MigrondiExt.IMigrondiUI
  localMigrondiFactory: MigrondiConfig * string -> MigrondiExt.IMigrondiUI
  migrondiCache: ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>
}

module MigrondiCache =
  let getOrAdd
    (cache: ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>)
    (projectId: Guid)
    (factory: unit -> MigrondiExt.IMigrondiUI)
    =
    cache.GetOrAdd(projectId, fun _ -> factory())

  let invalidate
    (cache: ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>)
    (projectId: Guid)
    =
    cache.TryRemove(projectId) |> ignore

  let clear(cache: ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>) =
    cache.Clear()

module McpRuntime =

  let findProject
    (lProjects: ILocalProjectRepository)
    (vProjects: IVirtualProjectRepository)
    (projectId: Guid)
    : CancellableTask<Project option> =
    cancellableTask {
      let! local = lProjects.GetProjectById projectId

      match local with
      | Some p -> return Some(Project.Local p)
      | None ->
        let! vproj = vProjects.GetProjectById projectId
        return vproj |> Option.map Project.Virtual
    }

  let getMigrondi
    (env: McpEnvironment)
    (project: Project)
    : MigrondiExt.IMigrondiUI option =
    match project with
    | Project.Local p ->
      match p.config with
      | None -> None
      | Some config ->
        let rootDir = IO.Path.GetDirectoryName p.migrondiConfigPath |> nonNull

        MigrondiCache.getOrAdd env.migrondiCache p.id (fun () ->
          env.localMigrondiFactory(config, rootDir))
        |> Some
    | Project.Virtual p ->
      let config = p.ToMigrondiConfig()
      let rootDir = "migrondi-ui://projects/virtual/"

      MigrondiCache.getOrAdd env.migrondiCache p.id (fun () ->
        env.vMigrondiFactory(config, rootDir, p.id))
      |> Some
