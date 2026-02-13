module MigrondiUI.McpServer

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Text.Json.Nodes
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

open ModelContextProtocol
open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

open IcedTasks

open JDeck
open JDeck.Encoding

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
    migrationName: string * ct: CancellationToken -> Task<CallToolResult>

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
    name: string *
    upContent: string *
    downContent: string *
    ct: CancellationToken ->
      Task<CallToolResult>

type DeleteMigrationDelegate =
  delegate of name: string * ct: CancellationToken -> Task<CallToolResult>

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

  let clear (cache: ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>) =
    cache.Clear()

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

      MigrondiCache.getOrAdd
        env.migrondiCache
        p.id
        (fun () -> env.localMigrondiFactory(config, rootDir))
      |> Some
  | Project.Virtual p ->
    let config = p.ToMigrondiConfig()
    let rootDir = "migrondi-ui://projects/virtual/"

    MigrondiCache.getOrAdd
      env.migrondiCache
      p.id
      (fun () -> env.vMigrondiFactory(config, rootDir, p.id))
    |> Some

module McpResults =

  type LocalProjectSummary = {
    id: Guid
    name: string
    description: string
    configPath: string
    hasValidConfig: bool
  } with

    static member Encoder: Encoder<LocalProjectSummary> =
      fun p ->
        Json.object [
          "id", Encode.guid p.id
          "name", Encode.string p.name
          "description", Encode.string p.description
          "configPath", Encode.string p.configPath
          "hasValidConfig", Encode.boolean p.hasValidConfig
        ]

    static member FromLocalProject(p: LocalProject) = {
      id = p.id
      name = p.name
      description = defaultArg p.description ""
      configPath = p.migrondiConfigPath
      hasValidConfig = p.config.IsSome
    }

  type VirtualProjectSummary = {
    id: Guid
    name: string
    description: string
    driver: string
    tableName: string
  } with

    static member Encoder: Encoder<VirtualProjectSummary> =
      fun p ->
        Json.object [
          "id", Encode.guid p.id
          "name", Encode.string p.name
          "description", Encode.string p.description
          "driver", Encode.string p.driver
          "tableName", Encode.string p.tableName
        ]

    static member FromVirtualProject(p: VirtualProject) = {
      id = p.id
      name = p.name
      description = defaultArg p.description ""
      driver = p.driver.AsString
      tableName = p.tableName
    }

  type ListProjectsResult = {
    local: LocalProjectSummary list
    virtualProjects: VirtualProjectSummary list
  } with

    static member Encoder: Encoder<ListProjectsResult> =
      fun r ->
        Json.object [
          "local", Json.sequence(r.local, LocalProjectSummary.Encoder)
          "virtualProjects",
          Json.sequence(r.virtualProjects, VirtualProjectSummary.Encoder)
        ]

    static member Empty = { local = []; virtualProjects = [] }

  type ProjectConfigOutput = {
    connection: string
    migrations: string
    tableName: string
    driver: string
  } with

    static member Encoder: Encoder<ProjectConfigOutput> =
      fun c ->
        Json.object [
          "connection", Encode.string c.connection
          "migrations", Encode.string c.migrations
          "tableName", Encode.string c.tableName
          "driver", Encode.string c.driver
        ]

    static member FromConfig(c: MigrondiConfig) = {
      connection = c.connection
      migrations = c.migrations
      tableName = c.tableName
      driver = c.driver.AsString
    }

  type LocalProjectDetail = {
    id: Guid
    name: string
    description: string
    configPath: string
    config: ProjectConfigOutput option
    kind: string
  } with

    static member Encoder: Encoder<LocalProjectDetail> =
      fun p ->
        match p.config with
        | Some c ->
          Json.object [
            "id", Encode.guid p.id
            "name", Encode.string p.name
            "description", Encode.string p.description
            "configPath", Encode.string p.configPath
            "config", ProjectConfigOutput.Encoder c
            "kind", Encode.string p.kind
          ]
        | None ->
          Json.object [
            "id", Encode.guid p.id
            "name", Encode.string p.name
            "description", Encode.string p.description
            "configPath", Encode.string p.configPath
            "kind", Encode.string p.kind
          ]

    static member FromLocalProject(p: LocalProject) = {
      id = p.id
      name = p.name
      description = defaultArg p.description ""
      configPath = p.migrondiConfigPath
      config = p.config |> Option.map ProjectConfigOutput.FromConfig
      kind = "local"
    }

  type VirtualProjectDetail = {
    id: Guid
    name: string
    description: string
    connection: string
    driver: string
    tableName: string
    projectId: Guid
    kind: string
  } with

    static member Encoder: Encoder<VirtualProjectDetail> =
      fun p ->
        Json.object [
          "id", Encode.guid p.id
          "name", Encode.string p.name
          "description", Encode.string p.description
          "connection", Encode.string p.connection
          "driver", Encode.string p.driver
          "tableName", Encode.string p.tableName
          "projectId", Encode.guid p.projectId
          "kind", Encode.string p.kind
        ]

    static member FromVirtualProject(p: VirtualProject) = {
      id = p.id
      name = p.name
      description = defaultArg p.description ""
      connection = p.connection
      driver = p.driver.AsString
      tableName = p.tableName
      projectId = p.projectId
      kind = "virtual"
    }

  type GetProjectResult =
    | LocalProject of LocalProjectDetail
    | VirtualProject of VirtualProjectDetail
    | ProjectNotFound of string

    static member Encoder: Encoder<GetProjectResult> =
      fun result ->
        match result with
        | LocalProject p -> LocalProjectDetail.Encoder p
        | VirtualProject p -> VirtualProjectDetail.Encoder p
        | ProjectNotFound err -> Json.object [ "error", Encode.string err ]

  type MigrationStatusOutput = {
    name: string
    timestamp: int64
    status: string
    fullName: string
  } with

    static member Encoder: Encoder<MigrationStatusOutput> =
      fun m ->
        Json.object [
          "name", Encode.string m.name
          "timestamp", Encode.int64 m.timestamp
          "status", Encode.string m.status
          "fullName", Encode.string m.fullName
        ]

    static member FromMigrationStatus(status: MigrationStatus) =
      let statusStr, migration =
        match status with
        | Applied m -> "Applied", m
        | Pending m -> "Pending", m

      {
        name = migration.name
        timestamp = migration.timestamp
        status = statusStr
        fullName = $"{migration.timestamp}_{migration.name}"
      }

  type ListMigrationsResult = {
    migrations: MigrationStatusOutput array
  } with

    static member Encoder: Encoder<ListMigrationsResult> =
      fun r ->
        Json.object [
          "migrations",
          Json.sequence(r.migrations, MigrationStatusOutput.Encoder)
        ]

    static member Empty = { migrations = Array.empty }

    static member FromMigrations(migrations: IReadOnlyList<MigrationStatus>) = {
      migrations = [|
        for m in migrations -> MigrationStatusOutput.FromMigrationStatus m
      |]
    }

  type MigrationDetail = {
    id: Guid
    name: string
    timestamp: int64
    upContent: string
    downContent: string
    manualTransaction: bool
    projectId: Guid
    fullName: string
  } with

    static member Encoder: Encoder<MigrationDetail> =
      fun m ->
        Json.object [
          "id", Encode.guid m.id
          "name", Encode.string m.name
          "timestamp", Encode.int64 m.timestamp
          "upContent", Encode.string m.upContent
          "downContent", Encode.string m.downContent
          "manualTransaction", Encode.boolean m.manualTransaction
          "projectId", Encode.guid m.projectId
          "fullName", Encode.string m.fullName
        ]

    static member FromVirtualMigration(m: VirtualMigration) = {
      id = m.id
      name = m.name
      timestamp = m.timestamp
      upContent = m.upContent
      downContent = m.downContent
      manualTransaction = m.manualTransaction
      projectId = m.projectId
      fullName = $"{m.timestamp}_{m.name}"
    }

  type GetMigrationResult =
    | MigrationFound of MigrationDetail
    | MigrationNotFound of string

    static member Encoder: Encoder<GetMigrationResult> =
      fun result ->
        match result with
        | MigrationFound m -> MigrationDetail.Encoder m
        | MigrationNotFound err -> Json.object [ "error", Encode.string err ]

  type MigrationPreview = {
    name: string
    timestamp: int64
    upContent: string
    downContent: string
    fullName: string
  } with

    static member Encoder: Encoder<MigrationPreview> =
      fun m ->
        Json.object [
          "name", Encode.string m.name
          "timestamp", Encode.int64 m.timestamp
          "upContent", Encode.string m.upContent
          "downContent", Encode.string m.downContent
          "fullName", Encode.string m.fullName
        ]

    static member FromMigration(m: Migration) = {
      name = m.name
      timestamp = m.timestamp
      upContent = m.upContent
      downContent = m.downContent
      fullName = $"{m.timestamp}_{m.name}"
    }

  type DryRunResult = {
    count: int
    migrations: MigrationPreview array
  } with

    static member Encoder: Encoder<DryRunResult> =
      fun r ->
        Json.object [
          "count", Encode.int r.count
          "migrations", Json.sequence(r.migrations, MigrationPreview.Encoder)
        ]

    static member Empty = { count = 0; migrations = Array.empty }

    static member FromMigrations(migrations: IReadOnlyList<Migration>) = {
      count = migrations.Count
      migrations = [| for m in migrations -> MigrationPreview.FromMigration m |]
    }

  type SuccessResult = {
    success: bool
    message: string
  } with

    static member Encoder: Encoder<SuccessResult> =
      fun r ->
        Json.object [
          "success", Encode.boolean r.success
          "message", Encode.string r.message
        ]

    static member Ok(message: string) = { success = true; message = message }

    static member Error(message: string) = {
      success = false
      message = message
    }

  type AppliedMigration = {
    name: string
    timestamp: int64
    fullName: string
  } with

    static member Encoder: Encoder<AppliedMigration> =
      fun m ->
        Json.object [
          "name", Encode.string m.name
          "timestamp", Encode.int64 m.timestamp
          "fullName", Encode.string m.fullName
        ]

    static member FromMigration(m: Migration) = {
      name = m.name
      timestamp = m.timestamp
      fullName = $"{m.timestamp}_{m.name}"
    }

    static member FromMigrationRecord(r: MigrationRecord) = {
      name = r.name
      timestamp = r.timestamp
      fullName = $"{r.timestamp}_{r.name}"
    }

  type MigrationsResult = {
    success: bool
    message: string
    count: int
    migrations: AppliedMigration array
  } with

    static member Encoder: Encoder<MigrationsResult> =
      fun r ->
        Json.object [
          "success", Encode.boolean r.success
          "message", Encode.string r.message
          "count", Encode.int r.count
          "migrations", Json.sequence(r.migrations, AppliedMigration.Encoder)
        ]

    static member Ok(count, migrations) = {
      success = true
      message = ""
      count = count
      migrations = migrations
    }

    static member Error(message) = {
      success = false
      message = message
      count = 0
      migrations = [||]
    }

    static member FromMigrations(migrations: IReadOnlyList<Migration>) =
      MigrationsResult.Ok(
        migrations.Count,
        [| for m in migrations -> AppliedMigration.FromMigration m |]
      )

    static member FromMigrationRecords
      (records: IReadOnlyList<MigrationRecord>)
      =
      MigrationsResult.Ok(
        records.Count,
        [| for r in records -> AppliedMigration.FromMigrationRecord r |]
      )

  type ErrorResult = {
    error: string
  } with

    static member Encoder: Encoder<ErrorResult> =
      fun r -> Json.object [ "error", Encode.string r.error ]

    static member Create(error: string) = { error = error }

  type CreateMigrationResult =
    | MigrationCreated of
      {|
        id: Guid
        name: string
        timestamp: int64
        fullName: string
      |}
    | CreateMigrationError of string

    static member Encoder: Encoder<CreateMigrationResult> =
      fun result ->
        match result with
        | MigrationCreated m ->
          Json.object [
            "success", Encode.boolean true
            "migration",
            Json.object [
              "id", Encode.guid m.id
              "name", Encode.string m.name
              "timestamp", Encode.int64 m.timestamp
              "fullName", Encode.string m.fullName
            ]
          ]
        | CreateMigrationError err -> Json.object [ "error", Encode.string err ]

  type CreateProjectResult =
    | ProjectCreated of
      {|
        id: Guid
        name: string
        driver: string
        tableName: string
      |}
    | CreateProjectError of string

    static member Encoder: Encoder<CreateProjectResult> =
      fun result ->
        match result with
        | ProjectCreated p ->
          Json.object [
            "success", Encode.boolean true
            "project",
            Json.object [
              "id", Encode.guid p.id
              "name", Encode.string p.name
              "driver", Encode.string p.driver
              "tableName", Encode.string p.tableName
            ]
          ]
        | CreateProjectError err -> Json.object [ "error", Encode.string err ]

  type ExportResult =
    | ExportSuccess of {| path: string |}
    | ExportError of string

    static member Encoder: Encoder<ExportResult> =
      fun result ->
        match result with
        | ExportSuccess p ->
          Json.object [
            "success", Encode.boolean true
            "path", Encode.string p.path
          ]
        | ExportError err -> Json.object [ "error", Encode.string err ]

  type ImportResult =
    | ImportSuccess of {| projectId: Guid |}
    | ImportError of string

    static member Encoder: Encoder<ImportResult> =
      fun result ->
        match result with
        | ImportSuccess p ->
          Json.object [
            "success", Encode.boolean true
            "projectId", Encode.guid p.projectId
          ]
        | ImportError err -> Json.object [ "error", Encode.string err ]

module private McpResultMapper =

  open McpResults

  let fromEncoder (encoder: Encoder<'T>) (value: 'T) : CallToolResult =
    let node = encoder value

    let isError =
      match node with
      | :? JsonObject as obj -> obj.ContainsKey("error")
      | _ -> false

    CallToolResult(StructuredContent = node, IsError = isError)

module McpTools =

  let listProjects (env: McpEnvironment) (ct: CancellationToken) = task {
    let! localProjects = env.lProjects.GetProjects () ct
    let! vProjects = env.vProjects.GetProjects () ct

    let result: McpResults.ListProjectsResult = {
      local =
        localProjects
        |> List.map McpResults.LocalProjectSummary.FromLocalProject
      virtualProjects =
        vProjects
        |> List.map McpResults.VirtualProjectSummary.FromVirtualProject
    }

    return
      McpResultMapper.fromEncoder McpResults.ListProjectsResult.Encoder result
  }

  let getProject
    (env: McpEnvironment)
    (projectId: string)
    (ct: CancellationToken)
    =
    task {
      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResultMapper.fromEncoder
            McpResults.GetProjectResult.Encoder
            (McpResults.GetProjectResult.ProjectNotFound
              "projectId must be a valid GUID")
      | true, id ->
        let! localProject = env.lProjects.GetProjectById id ct

        match localProject with
        | Some p ->
          return
            McpResultMapper.fromEncoder
              McpResults.GetProjectResult.Encoder
              (McpResults.GetProjectResult.LocalProject(
                McpResults.LocalProjectDetail.FromLocalProject p
              ))
        | None ->
          let! vProject = env.vProjects.GetProjectById id ct

          match vProject with
          | Some p ->
            return
              McpResultMapper.fromEncoder
                McpResults.GetProjectResult.Encoder
                (McpResults.GetProjectResult.VirtualProject(
                  McpResults.VirtualProjectDetail.FromVirtualProject p
                ))
          | None ->
            return
              McpResultMapper.fromEncoder
                McpResults.GetProjectResult.Encoder
                (McpResults.GetProjectResult.ProjectNotFound
                  $"Project {projectId} not found")
    }

  let listMigrations
    (env: McpEnvironment)
    (projectId: string)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ -> return McpResults.ListMigrationsResult.Empty
        | true, id ->
          match! findProject env.lProjects env.vProjects id ct with
          | None -> return McpResults.ListMigrationsResult.Empty
          | Some project ->
            match getMigrondi env project with
            | None -> return McpResults.ListMigrationsResult.Empty
            | Some migrondi ->
              let! migrations = migrondi.MigrationsListAsync ct
              return McpResults.ListMigrationsResult.FromMigrations migrations
      }

      return
        McpResultMapper.fromEncoder
          McpResults.ListMigrationsResult.Encoder
          result
    }

  let getMigration
    (env: McpEnvironment)
    (migrationName: string)
    (ct: CancellationToken)
    =
    task {
      match! env.vProjects.GetMigrationByName migrationName ct with
      | None ->
        return
          McpResultMapper.fromEncoder
            McpResults.GetMigrationResult.Encoder
            (McpResults.GetMigrationResult.MigrationNotFound
              $"Migration '{migrationName}' not found")
      | Some m ->
        return
          McpResultMapper.fromEncoder
            McpResults.GetMigrationResult.Encoder
            (McpResults.GetMigrationResult.MigrationFound(
              McpResults.MigrationDetail.FromVirtualMigration m
            ))
    }

  let dryRunMigrations
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ -> return McpResults.DryRunResult.Empty
        | true, id ->
          match! findProject env.lProjects env.vProjects id ct with
          | None -> return McpResults.DryRunResult.Empty
          | Some project ->
            match getMigrondi env project with
            | None -> return McpResults.DryRunResult.Empty
            | Some migrondi ->
              let! migrations =
                match amount with
                | Some a ->
                  migrondi.DryRunUpAsync(amount = a, cancellationToken = ct)
                | None -> migrondi.DryRunUpAsync(cancellationToken = ct)

              return McpResults.DryRunResult.FromMigrations migrations
      }

      return McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder result
    }

  let dryRunRollback
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ -> return McpResults.DryRunResult.Empty
        | true, id ->
          match! findProject env.lProjects env.vProjects id ct with
          | None -> return McpResults.DryRunResult.Empty
          | Some project ->
            match getMigrondi env project with
            | None -> return McpResults.DryRunResult.Empty
            | Some migrondi ->
              let! migrations =
                match amount with
                | Some a ->
                  migrondi.DryRunDownAsync(amount = a, cancellationToken = ct)
                | None -> migrondi.DryRunDownAsync(cancellationToken = ct)

              return McpResults.DryRunResult.FromMigrations migrations
      }

      return McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder result
    }

module McpWriteTools =

  let runMigrations
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.MigrationsResult.Error "projectId must be a valid GUID"
        | true, id ->
          match! findProject env.lProjects env.vProjects id ct with
          | None ->
            return
              McpResults.MigrationsResult.Error $"Project {projectId} not found"
          | Some project ->
            match getMigrondi env project with
            | None ->
              return
                McpResults.MigrationsResult.Error
                  "Project has no valid configuration"
            | Some migrondi ->
              try
                let! migrations =
                  match amount with
                  | Some a ->
                    migrondi.RunUpAsync(amount = a, cancellationToken = ct)
                  | None -> migrondi.RunUpAsync(cancellationToken = ct)

                return
                  McpResults.MigrationsResult.FromMigrationRecords migrations
              with ex ->
                return
                  McpResults.MigrationsResult.Error
                    $"Failed to apply migrations: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder result
    }

  let runRollback
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.MigrationsResult.Error "projectId must be a valid GUID"
        | true, id ->
          match! findProject env.lProjects env.vProjects id ct with
          | None ->
            return
              McpResults.MigrationsResult.Error $"Project {projectId} not found"
          | Some project ->
            match getMigrondi env project with
            | None ->
              return
                McpResults.MigrationsResult.Error
                  "Project has no valid configuration"
            | Some migrondi ->
              try
                let! migrations =
                  match amount with
                  | Some a ->
                    migrondi.RunDownAsync(amount = a, cancellationToken = ct)
                  | None -> migrondi.RunDownAsync(cancellationToken = ct)

                return
                  McpResults.MigrationsResult.FromMigrationRecords migrations
              with ex ->
                return
                  McpResults.MigrationsResult.Error
                    $"Failed to rollback migrations: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder result
    }

  let createMigration
    (env: McpEnvironment)
    (projectId: string)
    (name: string)
    (upContent: string option)
    (downContent: string option)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.CreateMigrationResult.CreateMigrationError
              "projectId must be a valid GUID"
        | true, projectId ->
          match! env.vProjects.GetProjectById projectId ct with
          | None ->
            return
              McpResults.CreateMigrationResult.CreateMigrationError
                $"Virtual project {projectId} not found"
          | Some _ ->
            let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

            let migration: VirtualMigration = {
              id = Guid.NewGuid()
              name = name
              timestamp = timestamp
              upContent = defaultArg upContent ""
              downContent = defaultArg downContent ""
              projectId = projectId
              manualTransaction = false
            }

            try
              let! id = env.vProjects.InsertMigration migration ct

              return
                McpResults.CreateMigrationResult.MigrationCreated {|
                  id = id
                  name = migration.name
                  timestamp = migration.timestamp
                  fullName = $"{migration.timestamp}_{migration.name}"
                |}
            with ex ->
              return
                McpResults.CreateMigrationResult.CreateMigrationError
                  $"Failed to create migration: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder
          McpResults.CreateMigrationResult.Encoder
          result
    }

  let updateMigration
    (env: McpEnvironment)
    (name: string)
    (upContent: string)
    (downContent: string)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match! env.vProjects.GetMigrationByName name ct with
        | None ->
          return McpResults.SuccessResult.Error $"Migration '{name}' not found"
        | Some m ->
          let updatedMigration: VirtualMigration = {
            m with
                upContent = upContent
                downContent = downContent
          }

          try
            do! env.vProjects.UpdateMigration updatedMigration ct

            return
              McpResults.SuccessResult.Ok
                $"Migration '{name}' updated successfully"
          with ex ->
            return
              McpResults.SuccessResult.Error
                $"Failed to update migration: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  let deleteMigration
    (env: McpEnvironment)
    (name: string)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match! env.vProjects.GetMigrationByName name ct with
        | None ->
          return McpResults.SuccessResult.Error $"Migration '{name}' not found"
        | Some _ ->
          try
            do! env.vProjects.RemoveMigrationByName name ct

            return
              McpResults.SuccessResult.Ok
                $"Migration '{name}' deleted successfully"
          with ex ->
            return
              McpResults.SuccessResult.Error
                $"Failed to delete migration: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  let createVirtualProject
    (env: McpEnvironment)
    (name: string)
    (connection: string)
    (driver: string)
    (description: string option)
    (tableName: string option)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        let driverValue =
          try
            MigrondiDriver.FromString driver |> Some
          with _ ->
            None

        match driverValue with
        | None ->
          return
            McpResults.CreateProjectResult.CreateProjectError
              $"Invalid driver '{driver}'. Valid options: sqlite, postgres, mysql, mssql"
        | Some driver ->
          let newProject: NewVirtualProjectArgs = {
            name = name
            description = defaultArg description ""
            connection = connection
            tableName = defaultArg tableName "migrations"
            driver = driver
          }

          try
            let! projectId = env.vProjects.InsertProject newProject ct

            return
              McpResults.CreateProjectResult.ProjectCreated {|
                id = projectId
                name = name
                driver = driver.AsString
                tableName = newProject.tableName
              |}
          with ex ->
            return
              McpResults.CreateProjectResult.CreateProjectError
                $"Failed to create project: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder
          McpResults.CreateProjectResult.Encoder
          result
    }

  let updateVirtualProject
    (env: McpEnvironment)
    (projectId: string)
    (name: string option)
    (connection: string option)
    (tableName: string option)
    (driver: string option)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ ->
          return McpResults.SuccessResult.Error "projectId must be a valid GUID"
        | true, id ->
          match! env.vProjects.GetProjectById id ct with
          | None ->
            return
              McpResults.SuccessResult.Error
                $"Virtual project {projectId} not found"
          | Some p ->
            let driverValue =
              driver
              |> Option.bind(fun d ->
                try
                  MigrondiDriver.FromString d |> Some
                with _ ->
                  None
              )
              |> Option.defaultValue p.driver

            let updatedProject: VirtualProject = {
              p with
                  name = defaultArg name p.name
                  connection = defaultArg connection p.connection
                  tableName = defaultArg tableName p.tableName
                  driver = driverValue
            }

            try
              do! env.vProjects.UpdateProject updatedProject ct
              MigrondiCache.invalidate env.migrondiCache p.id

              return
                McpResults.SuccessResult.Ok
                  $"Project '{updatedProject.name}' updated successfully"
            with ex ->
              return
                McpResults.SuccessResult.Error
                  $"Failed to update project: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  let deleteProject
    (env: McpEnvironment)
    (projectId: string)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ ->
          return McpResults.ErrorResult.Create "projectId must be a valid GUID"
        | true, id ->
          match! env.lProjects.GetProjectById id ct with
          | Some _ ->
            return
              McpResults.ErrorResult.Create
                "Deleting local projects is not supported via MCP. Remove the project manually or use the GUI."
          | None ->
            match! env.vProjects.GetProjectById id ct with
            | None ->
              return
                McpResults.ErrorResult.Create $"Project {projectId} not found"
            | Some _ ->
              return
                McpResults.ErrorResult.Create
                  "Deleting virtual projects is not yet implemented"
      }

      return McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder result
    }

  let exportVirtualProject
    (env: McpEnvironment)
    (projectId: string)
    (exportPath: string)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.ExportResult.ExportError "projectId must be a valid GUID"
        | true, id ->
          match! env.vProjects.GetProjectById id ct with
          | None ->
            return
              McpResults.ExportResult.ExportError
                $"Virtual project {projectId} not found"
          | Some p ->
            try
              let! exportedPath = env.vfs.ExportToLocal (p.id, exportPath) ct

              return
                McpResults.ExportResult.ExportSuccess {| path = exportedPath |}
            with ex ->
              return
                McpResults.ExportResult.ExportError
                  $"Failed to export project: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.ExportResult.Encoder result
    }

  let importFromLocal
    (env: McpEnvironment)
    (configPath: string)
    (ct: CancellationToken)
    =
    task {
      let! result = task {
        try
          let! projectId = env.vfs.ImportFromLocal configPath ct

          return
            McpResults.ImportResult.ImportSuccess {| projectId = projectId |}
        with ex ->
          return
            McpResults.ImportResult.ImportError
              $"Failed to import project: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.ImportResult.Encoder result
    }

let parseArgs (argv: string[]) : McpOptions option =
  let hasFlag (flag: string) =
    argv
    |> Array.exists(fun a ->
      String.Equals(a, flag, StringComparison.OrdinalIgnoreCase)
    )

  let getPort () =
    argv
    |> Array.tryFindIndex(fun a ->
      String.Equals(a, "--http", StringComparison.OrdinalIgnoreCase)
    )
    |> Option.bind(fun i ->
      if i + 1 < argv.Length then
        match Int32.TryParse(argv.[i + 1]) with
        | true, port when port > 0 && port < 65536 -> Some port
        | _ -> None
      else
        None
    )
    |> Option.defaultValue 8080

  if hasFlag "--stdio" then
    Some {
      mode = Stdio
      readOnly = hasFlag "--readonly"
    }
  elif hasFlag "--http" then
    Some {
      mode = Http(getPort())
      readOnly = hasFlag "--readonly"
    }
  else
    None

type private McpHttpSession = {
  id: string
  transport: StreamableHttpServerTransport
  server: McpServer
  serverTask: Task
}

let private runHttpServer
  (port: int)
  (serverOptions: McpServerOptions)
  (loggerFactory: ILoggerFactory)
  (serviceProvider: IServiceProvider)
  (ct: CancellationToken)
  : Task<unit> =

  task {
    let sessions = ConcurrentDictionary<string, McpHttpSession>()
    use listener = new Net.HttpListener()
    listener.Prefixes.Add($"http://localhost:{port}/mcp/")
    listener.Start()

    use linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct)
    let runningTasks = ConcurrentBag<Task>()

    let createSession () : McpHttpSession =
      let sessionId = Guid.NewGuid().ToString("N")

      let transport =
        StreamableHttpServerTransport(loggerFactory, SessionId = sessionId)

      let server =
        McpServer.Create(
          transport,
          serverOptions,
          loggerFactory,
          serviceProvider
        )

      let serverTask = server.RunAsync(linkedCts.Token)

      {
        id = sessionId
        transport = transport
        server = server
        serverTask = serverTask
      }

    let getSession sessionId =
      match sessions.TryGetValue(sessionId) with
      | true, s -> Some s
      | false, _ -> None

    let removeSession sessionId =
      match sessions.TryGetValue(sessionId) with
      | true, session ->
        session.transport.DisposeAsync().AsTask() |> ignore
        session.server.DisposeAsync().AsTask() |> ignore
        sessions.TryRemove(sessionId) |> ignore
      | false, _ -> ()

    let cleanup () =
      for kvp in sessions do
        kvp.Value.transport.DisposeAsync().AsTask() |> ignore
        kvp.Value.server.DisposeAsync().AsTask() |> ignore

    let handleRequest (context: Net.HttpListenerContext) = task {
      let sessionId =
        match context.Request.Headers.["Mcp-Session-Id"] with
        | null -> None
        | id -> Some(string id)

      let sendError (statusCode: int) (message: string) =
        try
          context.Response.StatusCode <- statusCode
          use writer = new IO.StreamWriter(context.Response.OutputStream)
          writer.Write(message)
          writer.Flush()
        with _ ->
          ()

        context.Response.Close()

      match context.Request.HttpMethod with
      | "POST" ->
        try
          let session =
            match sessionId with
            | Some id ->
              match getSession id with
              | Some s -> s
              | None -> createSession()
            | None -> createSession()

          sessions.[session.id] <- session

          use stream = context.Request.InputStream
          use reader = new IO.StreamReader(stream)
          let! body = reader.ReadToEndAsync(linkedCts.Token)

          let message =
            System.Text.Json.JsonSerializer.Deserialize<JsonRpcMessage>(
              body,
              McpJsonUtilities.DefaultOptions
            )

          match message with
          | null -> sendError 400 "Invalid JSON-RPC message"
          | msg ->
            context.Response.Headers.Add("Mcp-Session-Id", session.id)
            use responseStream = context.Response.OutputStream

            let! wroteResponse =
              session.transport.HandlePostRequestAsync(
                msg,
                responseStream,
                linkedCts.Token
              )

            if not wroteResponse then
              context.Response.StatusCode <- 202
              context.Response.Close()
        with ex ->
          sendError 500 $"Internal server error: {ex.Message}"

      | "GET" ->
        match sessionId with
        | None -> sendError 400 "Mcp-Session-Id header is required"
        | Some id ->
          match getSession id with
          | None ->
            context.Response.StatusCode <- 404
            context.Response.Close()
          | Some session ->
            context.Response.Headers.Add("Mcp-Session-Id", session.id)
            context.Response.ContentType <- "text/event-stream"
            context.Response.Headers.Add("Cache-Control", "no-cache")

            try
              do!
                session.transport.HandleGetRequestAsync(
                  context.Response.OutputStream,
                  linkedCts.Token
                )
            with
            | :? OperationCanceledException -> ()
            | ex -> ()

      | "DELETE" ->
        match sessionId with
        | None -> sendError 400 "Mcp-Session-Id header is required"
        | Some id ->
          removeSession id
          context.Response.StatusCode <- 200
          context.Response.Close()

      | _ ->
        context.Response.StatusCode <- 405
        context.Response.Close()
    }

    try
      while not linkedCts.Token.IsCancellationRequested do
        let! context = listener.GetContextAsync().WaitAsync(linkedCts.Token)
        let t = handleRequest context
        runningTasks.Add(t)
    with :? OperationCanceledException ->
      ()

    do! Task.WhenAll(runningTasks.ToArray())
    listener.Stop()
    cleanup()
  }

let runMcpServer
  (connectionFactory: unit -> System.Data.IDbConnection)
  (options: McpOptions)
  (loggerFactory: ILoggerFactory)
  : Task<unit> =
  task {
    Migrations.GetMigrondi loggerFactory
    |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
    |> Migrations.Migrate

    let lProjects, vProjects = Projects.GetRepositories connectionFactory

    let vMigrondiFactory = MigrondiExt.getMigrondiUI(loggerFactory, vProjects)

    let localMigrondiFactory (config: MigrondiConfig, rootDir: string) =
      let mLogger = loggerFactory.CreateLogger<IMigrondi>()
      let migrondi = Migrondi.MigrondiFactory(config, rootDir, mLogger)
      MigrondiExt.wrapLocalMigrondi(migrondi, rootDir)

    let vfs =
      let logger = loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>()
      VirtualFs.getVirtualFs(logger, vProjects)

    let env: McpEnvironment = {
      lf = loggerFactory
      lProjects = lProjects
      vProjects = vProjects
      vfs = vfs
      vMigrondiFactory = vMigrondiFactory
      localMigrondiFactory = localMigrondiFactory
      migrondiCache = ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>()
    }

    let services = ServiceCollection()
    services.AddSingleton<ILoggerFactory>(loggerFactory) |> ignore
    services.AddSingleton<McpEnvironment>(env) |> ignore

    let serviceProvider = services.BuildServiceProvider()

    let createTool
      name
      title
      (readOnly: bool)
      (destructive: bool)
      (del: Delegate)
      =
      let options =
        McpServerToolCreateOptions(
          Services = serviceProvider,
          Name = name,
          Title = title,
          ReadOnly = readOnly,
          Destructive = destructive
        )

      McpServerTool.Create(del, options)

    let allToolCollection = McpServerPrimitiveCollection<McpServerTool>()

    // Helper wrappers that capture env
    let listProjectsFn ct = McpTools.listProjects env ct
    let getProjectFn pid ct = McpTools.getProject env pid ct
    let listMigrationsFn pid ct = McpTools.listMigrations env pid ct
    let getMigrationFn name ct = McpTools.getMigration env name ct

    let dryRunMigrationsFn pid amount ct =
      McpTools.dryRunMigrations env pid amount ct

    let dryRunRollbackFn pid amount ct =
      McpTools.dryRunRollback env pid amount ct

    // Read-only tools
    let readTools: McpServerTool list = [
      createTool
        "list_projects"
        "List Projects"
        true
        false
        (ListProjectsDelegate listProjectsFn)
      createTool
        "get_project"
        "Get Project"
        true
        false
        (GetProjectDelegate getProjectFn)
      createTool
        "list_migrations"
        "List Migrations"
        true
        false
        (ListMigrationsDelegate listMigrationsFn)
      createTool
        "get_migration"
        "Get Migration"
        true
        false
        (GetMigrationDelegate getMigrationFn)
      createTool
        "dry_run_migrations"
        "Preview Migrations"
        true
        false
        (DryRunMigrationsDelegate dryRunMigrationsFn)
      createTool
        "dry_run_rollback"
        "Preview Rollback"
        true
        false
        (DryRunMigrationsDelegate dryRunRollbackFn)
    ]

    for tool in readTools do
      allToolCollection.Add(tool) |> ignore

    // Write tools (only if not read-only mode)
    if not options.readOnly then

      let runMigrationsFn pid amount ct =
        McpWriteTools.runMigrations env pid amount ct

      let runRollbackFn pid amount ct =
        McpWriteTools.runRollback env pid amount ct

      let createMigrationFn pid name up down ct =
        McpWriteTools.createMigration env pid name up down ct

      let updateMigrationFn name up down ct =
        McpWriteTools.updateMigration env name up down ct

      let deleteMigrationFn name ct =
        McpWriteTools.deleteMigration env name ct

      let createVirtualProjectFn name conn driver desc tbl ct =
        McpWriteTools.createVirtualProject env name conn driver desc tbl ct

      let updateVirtualProjectFn pid name conn tbl driver ct =
        McpWriteTools.updateVirtualProject env pid name conn tbl driver ct

      let deleteProjectFn pid ct = McpWriteTools.deleteProject env pid ct

      let exportVirtualProjectFn pid path ct =
        McpWriteTools.exportVirtualProject env pid path ct

      let importFromLocalFn path ct =
        McpWriteTools.importFromLocal env path ct

      let writeTools: McpServerTool list = [
        createTool
          "run_migrations"
          "Apply Migrations"
          false
          true
          (RunMigrationsDelegate runMigrationsFn)
        createTool
          "run_rollback"
          "Rollback Migrations"
          false
          true
          (RunMigrationsDelegate runRollbackFn)
        createTool
          "create_migration"
          "Create Migration"
          false
          false
          (CreateMigrationDelegate createMigrationFn)
        createTool
          "update_migration"
          "Update Migration"
          false
          false
          (UpdateMigrationDelegate updateMigrationFn)
        createTool
          "delete_migration"
          "Delete Migration"
          false
          true
          (DeleteMigrationDelegate deleteMigrationFn)
        createTool
          "create_virtual_project"
          "Create Virtual Project"
          false
          false
          (CreateVirtualProjectDelegate createVirtualProjectFn)
        createTool
          "update_virtual_project"
          "Update Virtual Project"
          false
          false
          (UpdateVirtualProjectDelegate updateVirtualProjectFn)
        createTool
          "delete_project"
          "Delete Project"
          false
          true
          (DeleteProjectDelegate deleteProjectFn)
        createTool
          "export_virtual_project"
          "Export Virtual Project"
          false
          false
          (ExportVirtualProjectDelegate exportVirtualProjectFn)
        createTool
          "import_from_local"
          "Import from Local"
          false
          false
          (ImportFromLocalDelegate importFromLocalFn)
      ]

      for tool in writeTools do
        allToolCollection.Add(tool) |> ignore

    let serverOptions = McpServerOptions()

    serverOptions.ServerInfo <-
      Implementation(Name = "migrondi-mcp", Version = "1.2.0")

    serverOptions.ToolCollection <- allToolCollection

    match options.mode with
    | Stdio ->
      let transport = StdioServerTransport("migrondi-mcp", loggerFactory)

      use server =
        McpServer.Create(
          transport,
          serverOptions,
          loggerFactory,
          serviceProvider
        )

      do! server.RunAsync()
    | Http port ->
      do!
        runHttpServer
          port
          serverOptions
          loggerFactory
          serviceProvider
          CancellationToken.None
  }