namespace Migrondi.Mcp

#nowarn 3265

open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Nodes

open ModelContextProtocol.Protocol

open JDeck
open JDeck.Encoding

open Migrondi.Core

module McpResults =
  open MigrondiUI

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
    migrations: string
    tableName: string
    driver: string
  } with

    static member Encoder: Encoder<ProjectConfigOutput> =
      fun c ->
        Json.object [
          "migrations", Encode.string c.migrations
          "tableName", Encode.string c.tableName
          "driver", Encode.string c.driver
        ]

    static member FromConfig(c: MigrondiConfig) = {
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
          "driver", Encode.string p.driver
          "tableName", Encode.string p.tableName
          "projectId", Encode.guid p.projectId
          "kind", Encode.string p.kind
        ]

    static member FromVirtualProject(p: VirtualProject) = {
      id = p.id
      name = p.name
      description = defaultArg p.description ""
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
        | ProjectNotFound err ->
          Json.object(List.singleton("error", Encode.string err))

  /// The on-disk file name of a migration. Decoded names from the standard
  /// writers already carry the timestamp ("<timestamp>_<name>"), so the
  /// leading prefix is stripped and the file name composes exactly once,
  /// with the `.sql` extension the real file has. Hand-authored files
  /// whose NAME line has no timestamp pass through unchanged.
  let fileNameFor(timestamp: int64, name: string) =
    let prefix = $"{timestamp}_"

    let friendly =
      if name.StartsWith(prefix, StringComparison.Ordinal) then
        name.Substring(prefix.Length)
      else
        name

    $"{timestamp}_{friendly}.sql"

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
        fullName = fileNameFor(migration.timestamp, migration.name)
      }

  type ListMigrationsResult = {
    migrations: MigrationStatusOutput array
  } with

    static member Encoder: Encoder<ListMigrationsResult> =
      fun r ->
        Json.object(
          List.singleton(
            "migrations",
            Json.sequence(r.migrations, MigrationStatusOutput.Encoder)
          )
        )

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
      fullName = fileNameFor(m.timestamp, m.name)
    }

  type GetMigrationResult =
    | MigrationFound of MigrationDetail
    | MigrationNotFound of string

    static member Encoder: Encoder<GetMigrationResult> =
      fun result ->
        match result with
        | MigrationFound m -> MigrationDetail.Encoder m
        | MigrationNotFound err ->
          Json.object(List.singleton("error", Encode.string err))

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
      fullName = fileNameFor(m.timestamp, m.name)
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
      fullName = fileNameFor(m.timestamp, m.name)
    }

    static member FromMigrationRecord(r: MigrationRecord) = {
      name = r.name
      timestamp = r.timestamp
      fullName = fileNameFor(r.timestamp, r.name)
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
      fun r -> Json.object(List.singleton("error", Encode.string r.error))

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
        | CreateMigrationError err ->
          Json.object(List.singleton("error", Encode.string err))

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
        | CreateProjectError err ->
          Json.object(List.singleton("error", Encode.string err))

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
        | ExportError err ->
          Json.object(List.singleton("error", Encode.string err))

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
        | ImportError err ->
          Json.object(List.singleton("error", Encode.string err))

  /// Shape of the single-project `get_project` tool. It carries no binding
  /// knowledge: hosts fill the fields from their own resolved binding and
  /// must never put the connection string in any of them.
  type ProjectInfoResult = {
    name: string
    configPath: string
    driver: string
    migrationsFolder: string
    tableName: string
  } with

    static member Encoder: Encoder<ProjectInfoResult> =
      fun r ->
        Json.object [
          "name", Encode.string r.name
          "configPath", Encode.string r.configPath
          "driver", Encode.string r.driver
          "migrationsFolder", Encode.string r.migrationsFolder
          "tableName", Encode.string r.tableName
        ]

  /// Result of the stateless `init_project` tool: the created or adopted
  /// project, or why the directory was refused.
  type InitProjectResult =
    | ProjectInitialized of
      {|
        directory: string
        configPath: string
        mode: string
      |}
    | InitError of string

    static member Encoder: Encoder<InitProjectResult> =
      fun result ->
        match result with
        | ProjectInitialized p ->
          Json.object [
            "success", Encode.boolean true
            "directory", Encode.string p.directory
            "configPath", Encode.string p.configPath
            "mode", Encode.string p.mode
          ]
        | InitError err ->
          Json.object(List.singleton("error", Encode.string err))

  /// Result of the stateless `update_project_config` tool.
  type UpdateConfigResult =
    | ConfigUpdated of {| configPath: string |}
    | UpdateError of string

    static member Encoder: Encoder<UpdateConfigResult> =
      fun result ->
        match result with
        | ConfigUpdated p ->
          Json.object [
            "success", Encode.boolean true
            "configPath", Encode.string p.configPath
          ]
        | UpdateError err ->
          Json.object(List.singleton("error", Encode.string err))

module McpResultMapper =

  open McpResults

  let fromEncoder (encoder: Encoder<'T>) (value: 'T) : CallToolResult =
    let node = encoder value

    let isError =
      match node with
      | :? JsonObject as obj -> obj.ContainsKey("error")
      | _ -> false

    let element =
      node.Deserialize<Nullable<JsonElement>>(
        JsonSerializerOptions(RespectNullableAnnotations = true)
      )

    CallToolResult(StructuredContent = element, IsError = isError)
