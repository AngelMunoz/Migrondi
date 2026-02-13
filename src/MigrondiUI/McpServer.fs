module MigrondiUI.McpServer

open System
open System.Collections.Concurrent
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

type IMcpReadDeps =
  abstract member GetLocalProjects:
    ct: CancellationToken -> Task<LocalProject list>

  abstract member GetVirtualProjects:
    ct: CancellationToken -> Task<VirtualProject list>

  abstract member GetLocalProjectById:
    id: Guid -> ct: CancellationToken -> Task<LocalProject option>

  abstract member GetVirtualProjectById:
    id: Guid -> ct: CancellationToken -> Task<VirtualProject option>

  abstract member GetMigrationByName:
    name: string -> ct: CancellationToken -> Task<VirtualMigration option>

  abstract member GetMigrations:
    projectId: Guid -> ct: CancellationToken -> Task<VirtualMigration list>

  abstract member GetLocalMigrondi:
    config: MigrondiConfig * rootDir: string -> MigrondiExt.IMigrondiUI

  abstract member GetVirtualMigrondi:
    config: MigrondiConfig * projectId: Guid -> MigrondiExt.IMigrondiUI

type IMcpWriteDeps =
  inherit IMcpReadDeps

  abstract member InsertMigration:
    migration: VirtualMigration -> ct: CancellationToken -> Task<Guid>

  abstract member UpdateMigration:
    migration: VirtualMigration -> ct: CancellationToken -> Task<unit>

  abstract member RemoveMigrationByName:
    name: string -> ct: CancellationToken -> Task<unit>

  abstract member InsertProject:
    args: NewVirtualProjectArgs -> ct: CancellationToken -> Task<Guid>

  abstract member UpdateProject:
    project: VirtualProject -> ct: CancellationToken -> Task<unit>

  abstract member ExportToLocal:
    projectId: Guid -> path: string -> ct: CancellationToken -> Task<string>

  abstract member ImportFromLocal:
    configPath: string -> ct: CancellationToken -> Task<Guid>

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

  type ListProjectsResult = {
    local: LocalProjectSummary list
    virtualProjects: VirtualProjectSummary list
  } with

    static member Encoder: Encoder<ListProjectsResult> =
      fun r ->
        Json.object [
          "local", Json.sequence(r.local, LocalProjectSummary.Encoder)
          "virtual",
          Json.sequence(r.virtualProjects, VirtualProjectSummary.Encoder)
        ]

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

  type ListMigrationsResult = {
    migrations: MigrationStatusOutput array
  } with

    static member Encoder: Encoder<ListMigrationsResult> =
      fun r ->
        Json.object [
          "migrations",
          Json.sequence(r.migrations, MigrationStatusOutput.Encoder)
        ]

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

    static member Ok(count, migrations) =
      { success = true; message = ""; count = count; migrations = migrations }

    static member Error(message) =
      { success = false; message = message; count = 0; migrations = [||] }

  type ErrorResult = {
    error: string
  } with

    static member Encoder: Encoder<ErrorResult> =
      fun r -> Json.object [ "error", Encode.string r.error ]

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

module McpServerLogic =

  open McpResults

  let listProjects (deps: IMcpReadDeps) (ct: CancellationToken) = task {
    let! localProjects = deps.GetLocalProjects ct
    let! virtualProjects = deps.GetVirtualProjects ct

    let localSummaries =
      localProjects
      |> List.map(fun p -> {
        id = p.id
        name = p.name
        description = defaultArg p.description ""
        configPath = p.migrondiConfigPath
        hasValidConfig = p.config.IsSome
      })

    let virtualSummaries =
      virtualProjects
      |> List.map(fun p -> {
        id = p.id
        name = p.name
        description = defaultArg p.description ""
        driver = p.driver.AsString
        tableName = p.tableName
      })

    return {
      local = localSummaries
      virtualProjects = virtualSummaries
    }
  }

  let getProject
    (deps: IMcpReadDeps)
    (projectId: string)
    (ct: CancellationToken)
    =
    task {
      match Guid.TryParse(projectId) with
      | false, _ -> return ProjectNotFound "projectId must be a valid GUID"
      | true, id ->
        let! localProject = deps.GetLocalProjectById id ct

        match localProject with
        | Some p ->
          let configOutput =
            p.config
            |> Option.map(fun c -> {
              connection = c.connection
              migrations = c.migrations
              tableName = c.tableName
              driver = c.driver.AsString
            })

          return
            LocalProject {
              id = p.id
              name = p.name
              description = defaultArg p.description ""
              configPath = p.migrondiConfigPath
              config = configOutput
              kind = "local"
            }
        | None ->
          let! virtualProject = deps.GetVirtualProjectById id ct

          match virtualProject with
          | Some p ->
            return
              VirtualProject {
                id = p.id
                name = p.name
                description = defaultArg p.description ""
                connection = p.connection
                driver = p.driver.AsString
                tableName = p.tableName
                projectId = p.projectId
                kind = "virtual"
              }
          | None -> return ProjectNotFound $"Project {projectId} not found"
    }

  let listMigrations
    (deps: IMcpReadDeps)
    (projectId: Guid)
    =
    cancellableTask {
      let! localProject = deps.GetLocalProjectById projectId

      match localProject with
      | Some p ->
        match p.config with
        | None -> return { migrations = [||] }
        | Some config ->
          let rootDir = IO.Path.GetDirectoryName p.migrondiConfigPath |> nonNull
          let migrondi = deps.GetLocalMigrondi(config, rootDir)

          let! token = CancellableTask.getCancellationToken()
          let! migrations = migrondi.MigrationsListAsync token

          let result = [|
            for m in migrations do
              let statusStr, migration =
                match m with
                | Applied mig -> "Applied", mig
                | Pending mig -> "Pending", mig

              yield {
                name = migration.name
                timestamp = migration.timestamp
                status = statusStr
                fullName = $"{migration.timestamp}_{migration.name}"
              }
          |]

          return { migrations = result }
      | None ->
        let! virtualProject = deps.GetVirtualProjectById projectId

        match virtualProject with
        | None -> return { migrations = [||] }
        | Some p ->
          let config = p.ToMigrondiConfig()
          let migrondi = deps.GetVirtualMigrondi(config, p.id)

          let! token = CancellableTask.getCancellationToken()
          let! migrations = migrondi.MigrationsListAsync token

          let result = [|
            for m in migrations do
              let statusStr, migration =
                match m with
                | Applied mig -> "Applied", mig
                | Pending mig -> "Pending", mig

              yield {
                name = migration.name
                timestamp = migration.timestamp
                status = statusStr
                fullName = $"{migration.timestamp}_{migration.name}"
              }
          |]

          return { migrations = result }
    }

  let getMigration
    (deps: IMcpReadDeps)
    (migrationName: string)
    (ct: CancellationToken)
    =
    task {
      let! migration = deps.GetMigrationByName migrationName ct

      match migration with
      | None ->
        return MigrationNotFound $"Migration '{migrationName}' not found"
      | Some m ->
        return
          MigrationFound {
            id = m.id
            name = m.name
            timestamp = m.timestamp
            upContent = m.upContent
            downContent = m.downContent
            manualTransaction = m.manualTransaction
            projectId = m.projectId
            fullName = $"{m.timestamp}_{m.name}"
          }
    }

  let dryRunMigrations
    (deps: IMcpReadDeps)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      let! localProject = deps.GetLocalProjectById projectId

      match localProject with
      | Some p ->
        match p.config with
        | None -> return { count = 0; migrations = [||] }
        | Some config ->
          let rootDir = IO.Path.GetDirectoryName p.migrondiConfigPath |> nonNull
          let migrondi = deps.GetLocalMigrondi(config, rootDir)

          let! token = CancellableTask.getCancellationToken()
          let! migrations =
            match amount with
            | Some a -> migrondi.DryRunUpAsync(amount = a, cancellationToken = token)
            | None -> migrondi.DryRunUpAsync(cancellationToken = token)

          let result = [|
            for m in migrations do
              yield {
                name = m.name
                timestamp = m.timestamp
                upContent = m.upContent
                downContent = m.downContent
                fullName = $"{m.timestamp}_{m.name}"
              }
          |]

          return { count = migrations.Count; migrations = result }
      | None ->
        let! virtualProject = deps.GetVirtualProjectById projectId

        match virtualProject with
        | None -> return { count = 0; migrations = [||] }
        | Some p ->
          let config = p.ToMigrondiConfig()
          let migrondi = deps.GetVirtualMigrondi(config, p.id)

          let! token = CancellableTask.getCancellationToken()
          let! migrations =
            match amount with
            | Some a -> migrondi.DryRunUpAsync(amount = a, cancellationToken = token)
            | None -> migrondi.DryRunUpAsync(cancellationToken = token)

          let result = [|
            for m in migrations do
              yield {
                name = m.name
                timestamp = m.timestamp
                upContent = m.upContent
                downContent = m.downContent
                fullName = $"{m.timestamp}_{m.name}"
              }
          |]

          return { count = migrations.Count; migrations = result }
    }

  let dryRunRollback
    (deps: IMcpReadDeps)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      let! localProject = deps.GetLocalProjectById projectId

      match localProject with
      | Some p ->
        match p.config with
        | None -> return { count = 0; migrations = [||] }
        | Some config ->
          let rootDir = IO.Path.GetDirectoryName p.migrondiConfigPath |> nonNull
          let migrondi = deps.GetLocalMigrondi(config, rootDir)

          let! token = CancellableTask.getCancellationToken()
          let! migrations =
            match amount with
            | Some a -> migrondi.DryRunDownAsync(amount = a, cancellationToken = token)
            | None -> migrondi.DryRunDownAsync(cancellationToken = token)

          let result = [|
            for m in migrations do
              yield {
                name = m.name
                timestamp = m.timestamp
                upContent = m.upContent
                downContent = m.downContent
                fullName = $"{m.timestamp}_{m.name}"
              }
          |]

          return { count = migrations.Count; migrations = result }
      | None ->
        let! virtualProject = deps.GetVirtualProjectById projectId

        match virtualProject with
        | None -> return { count = 0; migrations = [||] }
        | Some p ->
          let config = p.ToMigrondiConfig()
          let migrondi = deps.GetVirtualMigrondi(config, p.id)

          let! token = CancellableTask.getCancellationToken()
          let! migrations =
            match amount with
            | Some a -> migrondi.DryRunDownAsync(amount = a, cancellationToken = token)
            | None -> migrondi.DryRunDownAsync(cancellationToken = token)

          let result = [|
            for m in migrations do
              yield {
                name = m.name
                timestamp = m.timestamp
                upContent = m.upContent
                downContent = m.downContent
                fullName = $"{m.timestamp}_{m.name}"
              }
          |]

          return { count = migrations.Count; migrations = result }
    }

  let runMigrations
    (deps: IMcpWriteDeps)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      let! localProject = deps.GetLocalProjectById projectId

      match localProject with
      | Some p ->
        match p.config with
        | None ->
          return MigrationsResult.Error($"Local project {projectId} has no valid config")
        | Some config ->
          let rootDir = IO.Path.GetDirectoryName p.migrondiConfigPath |> nonNull
          let migrondi = deps.GetLocalMigrondi(config, rootDir)

          try
            let! token = CancellableTask.getCancellationToken()

            let! result =
              match amount with
              | Some a ->
                migrondi.RunUpAsync(amount = a, cancellationToken = token)
              | None -> migrondi.RunUpAsync(cancellationToken = token)

            let migrations = [|
              for m in result do
                yield {
                  name = m.name
                  timestamp = m.timestamp
                  fullName = $"{m.timestamp}_{m.name}"
                }
            |]

            return MigrationsResult.Ok(result.Count, migrations)
          with ex ->
            return MigrationsResult.Error($"Failed to apply migrations: {ex.Message}")
      | None ->
        let! virtualProject = deps.GetVirtualProjectById projectId

        match virtualProject with
        | None ->
          return MigrationsResult.Error($"Project {projectId} not found")
        | Some p ->
          let config = p.ToMigrondiConfig()
          let migrondi = deps.GetVirtualMigrondi(config, p.id)

          try
            let! token = CancellableTask.getCancellationToken()

            let! result =
              match amount with
              | Some a ->
                migrondi.RunUpAsync(amount = a, cancellationToken = token)
              | None -> migrondi.RunUpAsync(cancellationToken = token)

            let migrations = [|
              for m in result do
                yield {
                  name = m.name
                  timestamp = m.timestamp
                  fullName = $"{m.timestamp}_{m.name}"
                }
            |]

            return MigrationsResult.Ok(result.Count, migrations)
          with ex ->
            return MigrationsResult.Error($"Failed to apply migrations: {ex.Message}")
    }

  let runRollback
    (deps: IMcpWriteDeps)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      let! localProject = deps.GetLocalProjectById projectId

      match localProject with
      | Some p ->
        match p.config with
        | None ->
          return MigrationsResult.Error($"Local project {projectId} has no valid config")
        | Some config ->
          let rootDir = IO.Path.GetDirectoryName p.migrondiConfigPath |> nonNull
          let migrondi = deps.GetLocalMigrondi(config, rootDir)

          try
            let! token = CancellableTask.getCancellationToken()

            let! result =
              match amount with
              | Some a ->
                migrondi.RunDownAsync(amount = a, cancellationToken = token)
              | None -> migrondi.RunDownAsync(cancellationToken = token)

            let migrations = [|
              for m in result do
                yield {
                  name = m.name
                  timestamp = m.timestamp
                  fullName = $"{m.timestamp}_{m.name}"
                }
            |]

            return MigrationsResult.Ok(result.Count, migrations)
          with ex ->
            return MigrationsResult.Error($"Failed to rollback migrations: {ex.Message}")
      | None ->
        let! virtualProject = deps.GetVirtualProjectById projectId

        match virtualProject with
        | None ->
          return MigrationsResult.Error($"Project {projectId} not found")
        | Some p ->
          let config = p.ToMigrondiConfig()
          let migrondi = deps.GetVirtualMigrondi(config, p.id)

          try
            let! token = CancellableTask.getCancellationToken()

            let! result =
              match amount with
              | Some a ->
                migrondi.RunDownAsync(amount = a, cancellationToken = token)
              | None -> migrondi.RunDownAsync(cancellationToken = token)

            let migrations = [|
              for m in result do
                yield {
                  name = m.name
                  timestamp = m.timestamp
                  fullName = $"{m.timestamp}_{m.name}"
                }
            |]

            return MigrationsResult.Ok(result.Count, migrations)
          with ex ->
            return MigrationsResult.Error($"Failed to rollback migrations: {ex.Message}")
    }

  let createMigration
    (deps: IMcpWriteDeps)
    (projectId: string)
    (name: string)
    (upContent: string option)
    (downContent: string option)
    (ct: CancellationToken)
    =
    task {
      match Guid.TryParse(projectId) with
      | false, _ -> return CreateMigrationError "projectId must be a valid GUID"
      | true, projectId ->
        let! virtualProject = deps.GetVirtualProjectById projectId ct

        match virtualProject with
        | None ->
          return CreateMigrationError $"Virtual project {projectId} not found"
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
            let! id = deps.InsertMigration migration ct

            return
              MigrationCreated {|
                id = id
                name = migration.name
                timestamp = migration.timestamp
                fullName = $"{migration.timestamp}_{migration.name}"
              |}
          with ex ->
            return
              CreateMigrationError $"Failed to create migration: {ex.Message}"
    }

  let updateMigration
    (deps: IMcpWriteDeps)
    (name: string)
    (upContent: string)
    (downContent: string)
    (ct: CancellationToken)
    =
    task {
      let! existing = deps.GetMigrationByName name ct

      match existing with
      | None ->
        return {
          success = false
          message = $"Migration '{name}' not found"
        }
      | Some m ->
        let updatedMigration: VirtualMigration = {
          m with
              upContent = upContent
              downContent = downContent
        }

        try
          do! deps.UpdateMigration updatedMigration ct

          return {
            success = true
            message = $"Migration '{name}' updated successfully"
          }
        with ex ->
          return {
            success = false
            message = $"Failed to update migration: {ex.Message}"
          }
    }

  let deleteMigration
    (deps: IMcpWriteDeps)
    (name: string)
    (ct: CancellationToken)
    =
    task {
      let! existing = deps.GetMigrationByName name ct

      match existing with
      | None ->
        return {
          success = false
          message = $"Migration '{name}' not found"
        }
      | Some _ ->
        try
          do! deps.RemoveMigrationByName name ct

          return {
            success = true
            message = $"Migration '{name}' deleted successfully"
          }
        with ex ->
          return {
            success = false
            message = $"Failed to delete migration: {ex.Message}"
          }
    }

  let createVirtualProject
    (deps: IMcpWriteDeps)
    (name: string)
    (connection: string)
    (driver: string)
    (description: string option)
    (tableName: string option)
    (ct: CancellationToken)
    =
    task {
      let driverValue =
        try
          MigrondiDriver.FromString driver |> Some
        with _ ->
          None

      match driverValue with
      | None ->
        return
          CreateProjectError
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
          let! projectId = deps.InsertProject newProject ct

          return
            ProjectCreated {|
              id = projectId
              name = name
              driver = driver.AsString
              tableName = newProject.tableName
            |}
        with ex ->
          return CreateProjectError $"Failed to create project: {ex.Message}"
    }

  let updateVirtualProject
    (deps: IMcpWriteDeps)
    (projectId: string)
    (name: string option)
    (connection: string option)
    (tableName: string option)
    (driver: string option)
    (ct: CancellationToken)
    =
    task {
      match Guid.TryParse(projectId) with
      | false, _ ->
        return {
          success = false
          message = "projectId must be a valid GUID"
        }
      | true, id ->
        let! existing = deps.GetVirtualProjectById id ct

        match existing with
        | None ->
          return {
            success = false
            message = $"Virtual project {projectId} not found"
          }
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
            do! deps.UpdateProject updatedProject ct

            return {
              success = true
              message = $"Project '{updatedProject.name}' updated successfully"
            }
          with ex ->
            return {
              success = false
              message = $"Failed to update project: {ex.Message}"
            }
    }

  let deleteProject
    (deps: IMcpWriteDeps)
    (projectId: string)
    (ct: CancellationToken)
    =
    task {
      match Guid.TryParse(projectId) with
      | false, _ ->
        return {
          error = "projectId must be a valid GUID"
        }
      | true, id ->
        let! localProject = deps.GetLocalProjectById id ct

        match localProject with
        | Some _ ->
          return {
            error =
              "Deleting local projects is not supported via MCP. Remove the project manually or use the GUI."
          }
        | None ->
          return {
            error = $"Project {projectId} not found or deletion not supported"
          }
    }

  let exportVirtualProject
    (deps: IMcpWriteDeps)
    (projectId: string)
    (exportPath: string)
    (ct: CancellationToken)
    =
    task {
      match Guid.TryParse(projectId) with
      | false, _ -> return ExportError "projectId must be a valid GUID"
      | true, id ->
        let! virtualProject = deps.GetVirtualProjectById id ct

        match virtualProject with
        | None -> return ExportError $"Virtual project {projectId} not found"
        | Some p ->
          try
            let! exportedPath = deps.ExportToLocal p.id exportPath ct
            return ExportSuccess {| path = exportedPath |}
          with ex ->
            return ExportError $"Failed to export project: {ex.Message}"
    }

  let importFromLocal
    (deps: IMcpWriteDeps)
    (configPath: string)
    (ct: CancellationToken)
    =
    task {
      try
        let! projectId = deps.ImportFromLocal configPath ct
        return ImportSuccess {| projectId = projectId |}
      with ex ->
        return ImportError $"Failed to import project: {ex.Message}"
    }

module McpDepsFactory =

  let createReadDeps
    (lpr: ILocalProjectRepository)
    (vpr: IVirtualProjectRepository)
    (localMigrondiFactory: MigrondiConfig * string -> MigrondiExt.IMigrondiUI)
    (virtualMigrondiFactory: MigrondiConfig * Guid -> MigrondiExt.IMigrondiUI)
    : IMcpReadDeps =

    { new IMcpReadDeps with
        member _.GetLocalProjects ct = lpr.GetProjects () ct
        member _.GetVirtualProjects ct = vpr.GetProjects () ct
        member _.GetLocalProjectById id ct = lpr.GetProjectById id ct

        member _.GetVirtualProjectById id ct = vpr.GetProjectById id ct

        member _.GetMigrationByName name ct = vpr.GetMigrationByName name ct

        member _.GetMigrations projectId ct = vpr.GetMigrations projectId ct

        member _.GetLocalMigrondi(config, rootDir) =
          localMigrondiFactory(config, rootDir)

        member _.GetVirtualMigrondi(config, projectId) =
          virtualMigrondiFactory(config, projectId)
    }

  let createWriteDeps
    (lpr: ILocalProjectRepository)
    (vpr: IVirtualProjectRepository)
    (vfs: VirtualFs.MigrondiUIFs)
    (localMigrondiFactory: MigrondiConfig * string -> MigrondiExt.IMigrondiUI)
    (virtualMigrondiFactory: MigrondiConfig * Guid -> MigrondiExt.IMigrondiUI)
    : IMcpWriteDeps =

    { new IMcpWriteDeps with
        member _.GetLocalProjects ct = lpr.GetProjects () ct
        member _.GetVirtualProjects ct = vpr.GetProjects () ct
        member _.GetLocalProjectById id ct = lpr.GetProjectById id ct

        member _.GetVirtualProjectById id ct = vpr.GetProjectById id ct

        member _.GetMigrationByName name ct = vpr.GetMigrationByName name ct

        member _.GetMigrations projectId ct = vpr.GetMigrations projectId ct

        member _.GetLocalMigrondi(config, rootDir) =
          localMigrondiFactory(config, rootDir)

        member _.GetVirtualMigrondi(config, projectId) =
          virtualMigrondiFactory(config, projectId)

        member _.InsertMigration migration ct = vpr.InsertMigration migration ct

        member _.UpdateMigration migration ct = vpr.UpdateMigration migration ct

        member _.RemoveMigrationByName name ct =
          vpr.RemoveMigrationByName name ct

        member _.InsertProject args ct = vpr.InsertProject args ct

        member _.UpdateProject project ct = vpr.UpdateProject project ct

        member _.ExportToLocal projectId path ct =
          vfs.ExportToLocal (projectId, path) ct

        member _.ImportFromLocal configPath ct =
          vfs.ImportFromLocal configPath ct
    }

module private McpResultMapper =

  open McpResults

  let fromEncoder (encoder: Encoder<'T>) (value: 'T) : CallToolResult =
    let node = encoder value

    let isError =
      match node with
      | :? JsonObject as obj -> obj.ContainsKey("error")
      | _ -> false

    CallToolResult(StructuredContent = node, IsError = isError)

[<McpServerToolType>]
type McpTools(deps: IMcpReadDeps) =

  [<McpServerTool(ReadOnly = true,
                  Name = "list_projects",
                  Title = "List Projects")>]
  member _.ListProjects(ct: CancellationToken) = task {
    let! result = McpServerLogic.listProjects deps ct

    return
      McpResultMapper.fromEncoder McpResults.ListProjectsResult.Encoder result
  }

  [<McpServerTool(ReadOnly = true, Name = "get_project", Title = "Get Project")>]
  member _.GetProject(projectId: string, ct: CancellationToken) = task {
    let! result = McpServerLogic.getProject deps projectId ct

    return
      McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder result
  }

  [<McpServerTool(ReadOnly = true,
                  Name = "list_migrations",
                  Title = "List Migrations")>]
  member _.ListMigrations(projectId: string, ?cancellationToken: CancellationToken) = task {
    let ct = defaultArg cancellationToken CancellationToken.None

    match Guid.TryParse projectId with
    | false, _ ->
      return
        McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder
          { migrations = [||] }
    | true, id ->
      let! result = McpServerLogic.listMigrations deps id ct
      return McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder result
  }

  [<McpServerTool(ReadOnly = true,
                  Name = "get_migration",
                  Title = "Get Migration")>]
  member _.GetMigration(migrationName: string, ct: CancellationToken) = task {
    let! result = McpServerLogic.getMigration deps migrationName ct

    return
      McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder result
  }

  [<McpServerTool(ReadOnly = true,
                  Name = "dry_run_migrations",
                  Title = "Preview Migrations")>]
  member _.DryRunMigrations
    (projectId: string, ?amount: int, ?cancellationToken: CancellationToken)
    =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
            { count = 0; migrations = [||] }
      | true, id ->
        let! result = McpServerLogic.dryRunMigrations deps id amount ct
        return McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder result
    }

  [<McpServerTool(ReadOnly = true,
                  Name = "dry_run_rollback",
                  Title = "Preview Rollback")>]
  member _.DryRunRollback
    (projectId: string, ?amount: int, ?cancellationToken: CancellationToken)
    =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
            { count = 0; migrations = [||] }
      | true, id ->
        let! result = McpServerLogic.dryRunRollback deps id amount ct
        return McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder result
    }

[<McpServerToolType>]
type McpWriteTools(deps: IMcpWriteDeps) =

  [<McpServerTool(ReadOnly = false,
                  Destructive = true,
                  Name = "run_migrations",
                  Title = "Apply Migrations")>]
  member _.RunMigrations
    (projectId: string, ?amount: int, ?cancellationToken: CancellationToken)
    =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
            (McpResults.MigrationsResult.Error "projectId must be a valid GUID")
      | true, id ->
        let! result = McpServerLogic.runMigrations deps id amount ct
        return McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder result
    }

  [<McpServerTool(ReadOnly = false,
                  Destructive = true,
                  Name = "run_rollback",
                  Title = "Rollback Migrations")>]
  member _.RunRollback
    (projectId: string, ?amount: int, ?cancellationToken: CancellationToken)
    =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
            (McpResults.MigrationsResult.Error "projectId must be a valid GUID")
      | true, id ->
        let! result = McpServerLogic.runRollback deps id amount ct
        return McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder result
    }

  [<McpServerTool(ReadOnly = false,
                  Name = "create_migration",
                  Title = "Create Migration")>]
  member _.CreateMigration
    (
      projectId: string,
      name: string,
      ?upContent: string,
      ?downContent: string,
      ?cancellationToken: CancellationToken
    ) =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      let! result =
        McpServerLogic.createMigration
          deps
          projectId
          name
          upContent
          downContent
          ct

      return
        McpResultMapper.fromEncoder
          McpResults.CreateMigrationResult.Encoder
          result
    }

  [<McpServerTool(ReadOnly = false,
                  Name = "update_migration",
                  Title = "Update Migration")>]
  member _.UpdateMigration
    (
      name: string,
      upContent: string,
      downContent: string,
      ?cancellationToken: CancellationToken
    ) =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      let! result =
        McpServerLogic.updateMigration deps name upContent downContent ct

      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  [<McpServerTool(ReadOnly = false,
                  Destructive = true,
                  Name = "delete_migration",
                  Title = "Delete Migration")>]
  member _.DeleteMigration
    (name: string, ?cancellationToken: CancellationToken)
    =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None
      let! result = McpServerLogic.deleteMigration deps name ct
      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  [<McpServerTool(ReadOnly = false,
                  Name = "create_virtual_project",
                  Title = "Create Virtual Project")>]
  member _.CreateVirtualProject
    (
      name: string,
      connection: string,
      driver: string,
      ?description: string,
      ?tableName: string,
      ?cancellationToken: CancellationToken
    ) =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      let! result =
        McpServerLogic.createVirtualProject
          deps
          name
          connection
          driver
          description
          tableName
          ct

      return
        McpResultMapper.fromEncoder
          McpResults.CreateProjectResult.Encoder
          result
    }

  [<McpServerTool(ReadOnly = false,
                  Name = "update_virtual_project",
                  Title = "Update Virtual Project")>]
  member _.UpdateVirtualProject
    (
      projectId: string,
      ?name: string,
      ?connection: string,
      ?tableName: string,
      ?driver: string,
      ?cancellationToken: CancellationToken
    ) =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      let! result =
        McpServerLogic.updateVirtualProject
          deps
          projectId
          name
          connection
          tableName
          driver
          ct

      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  [<McpServerTool(ReadOnly = false,
                  Destructive = true,
                  Name = "delete_project",
                  Title = "Delete Project")>]
  member _.DeleteProject
    (projectId: string, ?cancellationToken: CancellationToken)
    =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None
      let! result = McpServerLogic.deleteProject deps projectId ct
      return McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder result
    }

  [<McpServerTool(ReadOnly = false,
                  Name = "export_virtual_project",
                  Title = "Export Virtual Project")>]
  member _.ExportVirtualProject
    (
      projectId: string,
      exportPath: string,
      ?cancellationToken: CancellationToken
    ) =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None

      let! result =
        McpServerLogic.exportVirtualProject deps projectId exportPath ct

      return McpResultMapper.fromEncoder McpResults.ExportResult.Encoder result
    }

  [<McpServerTool(ReadOnly = false,
                  Name = "import_from_local",
                  Title = "Import from Local")>]
  member _.ImportFromLocal
    (configPath: string, ?cancellationToken: CancellationToken)
    =
    task {
      let ct = defaultArg cancellationToken CancellationToken.None
      let! result = McpServerLogic.importFromLocal deps configPath ct
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

      match context.Request.HttpMethod with
      | "POST" ->
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
          |> nonNull

        context.Response.Headers.Add("Mcp-Session-Id", session.id)
        use responseStream = context.Response.OutputStream

        let! wroteResponse =
          session.transport.HandlePostRequestAsync(
            message,
            responseStream,
            linkedCts.Token
          )

        if not wroteResponse then
          context.Response.StatusCode <- 202
          context.Response.Close()

      | "GET" ->
        match sessionId with
        | None ->
          context.Response.StatusCode <- 400
          use writer = new IO.StreamWriter(context.Response.OutputStream)
          writer.Write("Mcp-Session-Id header is required")
          writer.Flush()
          context.Response.Close()
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
            with :? OperationCanceledException ->
              ()

      | "DELETE" ->
        match sessionId with
        | None ->
          context.Response.StatusCode <- 400
          context.Response.Close()
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
        handleRequest context |> ignore
    with :? OperationCanceledException ->
      ()

    listener.Stop()
    cleanup()
  }

let runMcpServer
  (options: McpOptions)
  (loggerFactory: ILoggerFactory)
  : Task<unit> =
  task {
    Migrations.GetMigrondi loggerFactory
    |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
    |> Migrations.Migrate

    let lpr, vpr = Projects.GetRepositories Database.ConnectionFactory

    let baseVirtualFactory = MigrondiExt.getMigrondiUI(loggerFactory, vpr)
    let virtualMigrondiFactory (config: MigrondiConfig, projectId: Guid) =
      baseVirtualFactory(config, "migrondi-ui://projects/virtual/", projectId)

    let localMigrondiFactory (config: MigrondiConfig, rootDir: string) =
      let mLogger = loggerFactory.CreateLogger<Migrondi.Core.IMigrondi>()
      let migrondi = Migrondi.Core.Migrondi.MigrondiFactory(config, rootDir, mLogger)
      { new MigrondiExt.IMigrondiUI with
          member _.DryRunDown(?amount) = migrondi.DryRunDown(?amount = amount)
          member _.DryRunDownAsync(?amount, ?cancellationToken) =
            migrondi.DryRunDownAsync(?amount = amount, ?cancellationToken = cancellationToken)
          member _.DryRunUp(?amount) = migrondi.DryRunUp(?amount = amount)
          member _.DryRunUpAsync(?amount, ?cancellationToken) =
            migrondi.DryRunUpAsync(?amount = amount, ?cancellationToken = cancellationToken)
          member _.Initialize() = migrondi.Initialize()
          member _.InitializeAsync(?cancellationToken) =
            migrondi.InitializeAsync(?cancellationToken = cancellationToken)
          member _.MigrationsList() = migrondi.MigrationsList()
          member _.MigrationsListAsync(?cancellationToken) =
            migrondi.MigrationsListAsync(?cancellationToken = cancellationToken)
          member _.RunDown(?amount) = migrondi.RunDown(?amount = amount)
          member _.RunDownAsync(?amount, ?cancellationToken) =
            migrondi.RunDownAsync(?amount = amount, ?cancellationToken = cancellationToken)
          member _.RunNew(friendlyName, ?upContent, ?downContent, ?manualTransaction) =
            migrondi.RunNew(friendlyName, ?upContent = upContent, ?downContent = downContent, ?manualTransaction = manualTransaction)
          member _.RunNewAsync(friendlyName, ?upContent, ?downContent, ?manualTransaction, ?cancellationToken) =
            migrondi.RunNewAsync(friendlyName, ?upContent = upContent, ?downContent = downContent, ?manualTransaction = manualTransaction, ?cancellationToken = cancellationToken)
          member _.RunUp(?amount) = migrondi.RunUp(?amount = amount)
          member _.RunUpAsync(?amount, ?cancellationToken) =
            migrondi.RunUpAsync(?amount = amount, ?cancellationToken = cancellationToken)
          member _.RunUpdateAsync(migration, ?cancellationToken) =
            task { () }
          member _.ScriptStatus(migrationPath) = migrondi.ScriptStatus(migrationPath)
          member _.ScriptStatusAsync(migrationPath, ?cancellationToken) =
            migrondi.ScriptStatusAsync(migrationPath, ?cancellationToken = cancellationToken)
      }

    let vfs =
      let logger = loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>()
      VirtualFs.getVirtualFs(logger, vpr)

    let readDeps =
      McpDepsFactory.createReadDeps lpr vpr localMigrondiFactory virtualMigrondiFactory

    let writeDeps =
      McpDepsFactory.createWriteDeps lpr vpr vfs localMigrondiFactory virtualMigrondiFactory

    let services = ServiceCollection()
    services.AddSingleton<ILoggerFactory>(loggerFactory) |> ignore

    services.AddSingleton<IMcpReadDeps>(readDeps) |> ignore
    services.AddSingleton<McpTools>() |> ignore

    if not options.readOnly then
      services.AddSingleton<IMcpWriteDeps>(writeDeps) |> ignore
      services.AddSingleton<McpWriteTools>() |> ignore

    let serviceProvider = services.BuildServiceProvider()

    let mcpTools = serviceProvider.GetRequiredService<McpTools>()

    let writeTools =
      if options.readOnly then
        None
      else
        Some(serviceProvider.GetRequiredService<McpWriteTools>())

    let readToolCollection = McpServerPrimitiveCollection<McpServerTool>()
    let createOptions = McpServerToolCreateOptions(Services = serviceProvider)

    for method in
      typeof<McpTools>
        .GetMethods(
          System.Reflection.BindingFlags.Instance
          ||| System.Reflection.BindingFlags.Public
        ) do
      if
        method.GetCustomAttributes(typeof<McpServerToolAttribute>, true).Length > 0
      then
        let tool = McpServerTool.Create(method, mcpTools, createOptions)
        readToolCollection.Add(tool) |> ignore

    let allToolCollection = McpServerPrimitiveCollection<McpServerTool>()

    for tool in readToolCollection do
      allToolCollection.Add(tool) |> ignore

    match writeTools with
    | Some wt ->
      for method in
        typeof<McpWriteTools>
          .GetMethods(
            System.Reflection.BindingFlags.Instance
            ||| System.Reflection.BindingFlags.Public
          ) do
        if
          method
            .GetCustomAttributes(typeof<McpServerToolAttribute>, true)
            .Length > 0
        then
          let tool = McpServerTool.Create(method, wt, createOptions)
          allToolCollection.Add(tool) |> ignore
    | None -> ()

    let serverOptions = McpServerOptions()

    serverOptions.ServerInfo <-
      Implementation(Name = "MigrondiUI", Version = "1.0.0")

    serverOptions.ToolCollection <- allToolCollection

    match options.mode with
    | Stdio ->
      let transport = StdioServerTransport("MigrondiUI", loggerFactory)

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