namespace Migrondi.Mcp

open System
open System.Threading
open System.Threading.Tasks

open ModelContextProtocol.Protocol

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

open ModelContextProtocol.Server

open MigrondiUI
open Migrondi.Core

open IcedTasks

// Strongly-typed delegates for the multi-project tools; constructed
// directly from the F# wrapper functions below.
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


module ServerBuilder =

  module ReadTools =
    let listProjectsFn env = cancellableTask {
      let! projects = McpTools.listProjects env

      let result: McpResults.ListProjectsResult = {
        local =
          projects
          |> List.choose(function
            | Local p -> Some(McpResults.LocalProjectSummary.FromLocalProject p)
            | Virtual _ -> None)
        virtualProjects =
          projects
          |> List.choose(function
            | Virtual p ->
              Some(McpResults.VirtualProjectSummary.FromVirtualProject p)
            | Local _ -> None)
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.ListProjectsResult.Encoder
    }

    let getProjectFn env (pid: string) = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.GetProjectResult.ProjectNotFound
            $"Invalid project ID: {pid}"
          |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder
      | true, guid ->
        let! project = McpTools.getProject env guid

        match project with
        | Some(Local p) ->
          return
            McpResults.LocalProjectDetail.FromLocalProject p
            |> McpResults.GetProjectResult.LocalProject
            |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder
        | Some(Virtual p) ->
          return
            McpResults.VirtualProjectDetail.FromVirtualProject p
            |> McpResults.GetProjectResult.VirtualProject
            |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder
        | None ->
          return
            McpResults.GetProjectResult.ProjectNotFound
              $"Project {pid} not found"
            |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder
    }

    let listMigrationsFn env (pid: string) = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.ListMigrationsResult.Empty
          |> McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder
      | true, guid ->
        let! migrations = McpTools.listMigrations env guid

        let result = {
          McpResults.ListMigrationsResult.migrations =
            migrations
            |> List.map McpResults.MigrationStatusOutput.FromMigrationStatus
            |> List.toArray
        }

        return
          result
          |> McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder
    }

    let getMigrationFn env (guid: string) name = cancellableTask {
      match Guid.TryParse guid with
      | false, _ ->
        return
          McpResults.GetMigrationResult.MigrationNotFound "Invalid project ID"
          |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
      | true, projectId ->
        let! result = McpTools.getMigration env projectId name

        match result with
        | Error McpTools.GetMigrationError.ProjectNotFound ->
          return
            McpResults.GetMigrationResult.MigrationNotFound
              $"Project {guid} not found"
            |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
        | Error McpTools.GetMigrationError.LocalProjectsNotSupported ->
          return
            McpResults.GetMigrationResult.MigrationNotFound
              "Get migration is only supported for virtual projects"
            |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
        | Error McpTools.GetMigrationError.MigrationNotFound ->
          return
            McpResults.GetMigrationResult.MigrationNotFound
              $"Migration '{name}' not found"
            |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
        | Ok m ->
          return
            m
            |> McpResults.MigrationDetail.FromVirtualMigration
            |> McpResults.GetMigrationResult.MigrationFound
            |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
    }

    let dryRunMigrationsFn env (pid: string) amount = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.DryRunResult.Empty
          |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
      | true, guid ->
        let! migrations = McpTools.dryRunMigrations env guid amount

        let result =
          if migrations.IsEmpty then
            McpResults.DryRunResult.Empty
          else
            {
              McpResults.DryRunResult.count = migrations.Length
              migrations =
                migrations
                |> List.map McpResults.MigrationPreview.FromMigration
                |> List.toArray
            }

        return
          result |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
    }

    let dryRunRollbackFn env (pid: string) amount = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.DryRunResult.Empty
          |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
      | true, guid ->
        let! migrations = McpTools.dryRunRollback env guid amount

        let result =
          if migrations.IsEmpty then
            McpResults.DryRunResult.Empty
          else
            {
              McpResults.DryRunResult.count = migrations.Length
              migrations =
                migrations
                |> List.map McpResults.MigrationPreview.FromMigration
                |> List.toArray
            }

        return
          result |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
    }

  module WriteTools =
    let runMigrationsFn env (pid: string) amount = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.MigrationsResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
      | true, guid ->
        let! result = McpTools.runMigrations env guid amount

        match result with
        | Error McpTools.RunMigrationsError.ProjectNotFound ->
          return
            McpResults.MigrationsResult.Error $"Project {pid} not found"
            |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
        | Error(McpTools.RunMigrationsError.ExecutionFailed msg) ->
          return
            McpResults.MigrationsResult.Error
              $"Failed to apply migrations: {msg}"
            |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
        | Ok migrations ->
          return
            migrations
            |> McpResults.MigrationsResult.FromMigrationRecords
            |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
    }

    let runRollbackFn env (pid: string) amount = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.MigrationsResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
      | true, guid ->
        let! result = McpTools.runRollback env guid amount

        match result with
        | Error McpTools.RunRollbackError.ProjectNotFound ->
          return
            McpResults.MigrationsResult.Error $"Project {pid} not found"
            |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
        | Error(McpTools.RunRollbackError.ExecutionFailed msg) ->
          return
            McpResults.MigrationsResult.Error
              $"Failed to rollback migrations: {msg}"
            |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
        | Ok migrations ->
          return
            migrations
            |> McpResults.MigrationsResult.FromMigrationRecords
            |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
    }

    let createMigrationFn env (pid: string) name up down = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.CreateMigrationResult.CreateMigrationError
            "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder
            McpResults.CreateMigrationResult.Encoder
      | true, guid ->
        let! result = McpTools.createMigration env guid name up down

        match result with
        | Error McpTools.CreateMigrationError.ProjectNotFound ->
          return
            McpResults.CreateMigrationResult.CreateMigrationError
              $"Project {pid} not found"
            |> McpResultMapper.fromEncoder
              McpResults.CreateMigrationResult.Encoder
        | Error(McpTools.CreateMigrationError.InvalidMigrationName msg) ->
          return
            McpResults.CreateMigrationResult.CreateMigrationError msg
            |> McpResultMapper.fromEncoder
              McpResults.CreateMigrationResult.Encoder
        | Error(McpTools.CreateMigrationError.CreationFailed msg) ->
          return
            McpResults.CreateMigrationResult.CreateMigrationError
              $"Failed to create migration: {msg}"
            |> McpResultMapper.fromEncoder
              McpResults.CreateMigrationResult.Encoder
        | Ok m ->
          return
            McpResults.CreateMigrationResult.MigrationCreated {|
              id = Guid.NewGuid()
              name = m.name
              timestamp = m.timestamp
              fullName = m.fullName
            |}
            |> McpResultMapper.fromEncoder
              McpResults.CreateMigrationResult.Encoder
    }

    let updateMigrationFn env (projectId: string) name up down = cancellableTask {
      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResults.SuccessResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
      | true, guid ->
        let! result = McpTools.updateMigration env guid name up down

        match result with
        | Error McpTools.UpdateMigrationError.ProjectNotFound ->
          return
            McpResults.SuccessResult.Error $"Project {projectId} not found"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Error McpTools.UpdateMigrationError.MigrationNotFound ->
          return
            McpResults.SuccessResult.Error $"Migration '{name}' not found"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Error McpTools.UpdateMigrationError.AlreadyApplied ->
          return
            McpResults.SuccessResult.Error
              $"Migration '{name}' has already been applied"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Error(McpTools.UpdateMigrationError.DatabaseError msg) ->
          return
            McpResults.SuccessResult.Error msg
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Ok _ ->
          return
            McpResults.SuccessResult.Ok
              $"Migration '{name}' updated successfully"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
    }

    let deleteMigrationFn env (projectId: string) name = cancellableTask {
      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResults.SuccessResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
      | true, guid ->
        let! result = McpTools.deleteMigration env guid name

        match result with
        | Error McpTools.DeleteMigrationError.ProjectNotFound ->
          return
            McpResults.SuccessResult.Error $"Project {projectId} not found"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Error McpTools.DeleteMigrationError.MigrationNotFound ->
          return
            McpResults.SuccessResult.Error $"Migration '{name}' not found"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Error McpTools.DeleteMigrationError.AlreadyApplied ->
          return
            McpResults.SuccessResult.Error
              $"Migration '{name}' has already been applied"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Error(McpTools.DeleteMigrationError.DatabaseError msg) ->
          return
            McpResults.SuccessResult.Error msg
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | Ok _ ->
          return
            McpResults.SuccessResult.Ok
              $"Migration '{name}' deleted successfully"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
    }

    let createVirtualProjectFn env name conn driver desc tbl = cancellableTask {
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
          |> McpResultMapper.fromEncoder McpResults.CreateProjectResult.Encoder
      | Some driver ->
        let! result =
          McpTools.createVirtualProject env name conn driver desc tbl

        match result with
        | Error(McpTools.CreateProjectError.CreationFailed msg) ->
          return
            McpResults.CreateProjectResult.CreateProjectError
              $"Failed to create project: {msg}"
            |> McpResultMapper.fromEncoder
              McpResults.CreateProjectResult.Encoder
        | Ok projectId ->
          let tableName = defaultArg tbl "migrations"

          return
            McpResults.CreateProjectResult.ProjectCreated {|
              id = projectId
              name = name
              driver = driver.AsString
              tableName = tableName
            |}
            |> McpResultMapper.fromEncoder
              McpResults.CreateProjectResult.Encoder
    }

    let updateVirtualProjectFn env (pid: string) name conn tbl driver =
      cancellableTask {
        match Guid.TryParse pid with
        | false, _ ->
          return
            McpResults.SuccessResult.Error "projectId must be a valid GUID"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
        | true, guid ->
          let driverValue =
            driver
            |> Option.bind(fun d ->
              try
                MigrondiDriver.FromString d |> Some
              with _ ->
                None)

          let! result =
            McpTools.updateVirtualProject env guid name conn tbl driverValue

          match result with
          | Error McpTools.UpdateProjectError.ProjectNotFound ->
            return
              McpResults.SuccessResult.Error $"Project {pid} not found"
              |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
          | Error McpTools.UpdateProjectError.LocalProjectsNotSupported ->
            return
              McpResults.SuccessResult.Error
                "Cannot update local projects via MCP"
              |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
          | Error(McpTools.UpdateProjectError.UpdateFailed msg) ->
            return
              McpResults.SuccessResult.Error $"Failed to update project: {msg}"
              |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
          | Ok _ ->
            let projectName = defaultArg name "Project"

            return
              McpResults.SuccessResult.Ok
                $"Project '{projectName}' updated successfully"
              |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
      }

    let deleteProjectFn env (pid: string) = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.ErrorResult.Create "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder
      | true, guid ->
        let! result = McpTools.deleteProject env guid

        match result with
        | Error McpTools.DeleteProjectError.ProjectNotFound ->
          return
            McpResults.ErrorResult.Create $"Project {pid} not found"
            |> McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder
        | Error McpTools.DeleteProjectError.HasAppliedMigrations ->
          return
            McpResults.ErrorResult.Create
              "Cannot delete project with applied migrations"
            |> McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder
        | Ok _ ->
          return
            McpResults.SuccessResult.Ok $"Project {pid} deleted"
            |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
    }

    let exportVirtualProjectFn env (pid: string) path = cancellableTask {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.ExportResult.ExportError "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.ExportResult.Encoder
      | true, guid ->
        let! result = McpTools.exportVirtualProject env guid path

        match result with
        | Error McpTools.ExportProjectError.ProjectNotFound ->
          return
            McpResults.ExportResult.ExportError $"Project {pid} not found"
            |> McpResultMapper.fromEncoder McpResults.ExportResult.Encoder
        | Error McpTools.ExportProjectError.LocalProjectsNotSupported ->
          return
            McpResults.ExportResult.ExportError "Cannot export local projects"
            |> McpResultMapper.fromEncoder McpResults.ExportResult.Encoder
        | Error(McpTools.ExportProjectError.ExportFailed msg) ->
          return
            McpResults.ExportResult.ExportError
              $"Failed to export project: {msg}"
            |> McpResultMapper.fromEncoder McpResults.ExportResult.Encoder
        | Ok exportedPath ->
          return
            McpResults.ExportResult.ExportSuccess {| path = exportedPath |}
            |> McpResultMapper.fromEncoder McpResults.ExportResult.Encoder
    }

    let importFromLocalFn env path = cancellableTask {
      let! result = McpTools.importFromLocal env path

      match result with
      | Error(McpTools.ImportProjectError.ImportFailed msg) ->
        return
          McpResults.ImportResult.ImportError $"Failed to import project: {msg}"
          |> McpResultMapper.fromEncoder McpResults.ImportResult.Encoder
      | Ok projectId ->
        return
          McpResults.ImportResult.ImportSuccess {| projectId = projectId |}
          |> McpResultMapper.fromEncoder McpResults.ImportResult.Encoder
    }

  open ModelContextProtocol.Protocol

  let parseArgs(argv: string[]) : McpOptions option =
    let hasFlag(flag: string) =
      argv
      |> Array.exists(fun a ->
        String.Equals(a, flag, StringComparison.OrdinalIgnoreCase))

    let getPort() =
      argv
      |> Array.tryFindIndex(fun a ->
        String.Equals(a, "--http", StringComparison.OrdinalIgnoreCase))
      |> Option.bind(fun i ->
        if i + 1 < argv.Length then
          match Int32.TryParse(argv[i + 1]) with
          | true, port when port > 0 && port < 65536 -> Some port
          | _ -> None
        else
          None)
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

  let createTool
    (serviceProvider: IServiceProvider)
    (spec: ToolSpec)
    (del: Delegate)
    : McpServerTool =

    McpServerTool.Create(
      del,
      McpServerToolCreateOptions(
        Services = serviceProvider,
        Name = spec.Name,
        Title = spec.Title,
        ReadOnly = spec.ReadOnly,
        Destructive = spec.Destructive,
        Idempotent = spec.Idempotent,
        OpenWorld = spec.OpenWorld
      )
    )

  let readSpec name title = {
    Name = name
    Title = title
    ReadOnly = true
    Destructive = false
    Idempotent = true
    OpenWorld = false
  }

  let writeSpec name title destructive = {
    Name = name
    Title = title
    ReadOnly = false
    Destructive = destructive
    Idempotent = false
    OpenWorld = false
  }

  let createReadTools
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    : McpServerTool list = [
    createTool
      serviceProvider
      (readSpec "list_projects" "List Projects")
      (ListProjectsDelegate(fun ct -> ReadTools.listProjectsFn env ct))
    createTool
      serviceProvider
      (readSpec "get_project" "Get Project")
      (GetProjectDelegate(fun projectId ct ->
        ReadTools.getProjectFn env projectId ct))
    createTool
      serviceProvider
      (readSpec "list_migrations" "List Migrations")
      (ListMigrationsDelegate(fun projectId ct ->
        ReadTools.listMigrationsFn env projectId ct))
    createTool
      serviceProvider
      (readSpec "get_migration" "Get Migration")
      (GetMigrationDelegate(fun projectId migrationName ct ->
        ReadTools.getMigrationFn env projectId migrationName ct))
    createTool
      serviceProvider
      (readSpec "dry_run_migrations" "Preview Migrations")
      (DryRunMigrationsDelegate(fun projectId amount ct ->
        ReadTools.dryRunMigrationsFn env projectId amount ct))
    createTool
      serviceProvider
      (readSpec "dry_run_rollback" "Preview Rollback")
      (DryRunMigrationsDelegate(fun projectId amount ct ->
        ReadTools.dryRunRollbackFn env projectId amount ct))
  ]

  let createWriteTools
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    : McpServerTool list = [
    createTool
      serviceProvider
      (writeSpec "run_migrations" "Apply Migrations" true)
      (RunMigrationsDelegate(fun projectId amount ct ->
        WriteTools.runMigrationsFn env projectId amount ct))
    createTool
      serviceProvider
      (writeSpec "run_rollback" "Rollback Migrations" true)
      (RunMigrationsDelegate(fun projectId amount ct ->
        WriteTools.runRollbackFn env projectId amount ct))
    createTool
      serviceProvider
      (writeSpec "create_migration" "Create Migration" false)
      (CreateMigrationDelegate(fun projectId name upContent downContent ct ->
        WriteTools.createMigrationFn
          env
          projectId
          name
          upContent
          downContent
          ct))
    createTool
      serviceProvider
      (writeSpec "update_migration" "Update Migration" false)
      (UpdateMigrationDelegate(fun projectId name upContent downContent ct ->
        WriteTools.updateMigrationFn
          env
          projectId
          name
          upContent
          downContent
          ct))
    createTool
      serviceProvider
      (writeSpec "delete_migration" "Delete Migration" true)
      (DeleteMigrationDelegate(fun projectId name ct ->
        WriteTools.deleteMigrationFn env projectId name ct))
    createTool
      serviceProvider
      (writeSpec "create_virtual_project" "Create Virtual Project" false)
      (CreateVirtualProjectDelegate
        (fun name connection driver description tableName ct ->
          WriteTools.createVirtualProjectFn
            env
            name
            connection
            driver
            description
            tableName
            ct))
    createTool
      serviceProvider
      (writeSpec "update_virtual_project" "Update Virtual Project" false)
      (UpdateVirtualProjectDelegate
        (fun projectId name connection tableName driver ct ->
          WriteTools.updateVirtualProjectFn
            env
            projectId
            name
            connection
            tableName
            driver
            ct))
    createTool
      serviceProvider
      (writeSpec "delete_project" "Delete Project" true)
      (DeleteProjectDelegate(fun projectId ct ->
        WriteTools.deleteProjectFn env projectId ct))
    createTool
      serviceProvider
      (writeSpec "export_virtual_project" "Export Virtual Project" false)
      (ExportVirtualProjectDelegate(fun projectId exportPath ct ->
        WriteTools.exportVirtualProjectFn env projectId exportPath ct))
    createTool
      serviceProvider
      (writeSpec "import_from_local" "Import from Local" false)
      (ImportFromLocalDelegate(fun configPath ct ->
        WriteTools.importFromLocalFn env configPath ct))
  ]

  let buildToolCollection
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    (readOnly: bool)
    : McpServerPrimitiveCollection<McpServerTool> =
    let collection = McpServerPrimitiveCollection<McpServerTool>()

    for tool in createReadTools env serviceProvider do
      collection.Add(tool)

    if not readOnly then
      for tool in createWriteTools env serviceProvider do
        collection.Add(tool)

    collection

  let createServerOptions
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    (readOnly: bool)
    : McpServerOptions =
    let options = McpServerOptions()

    options.ServerInfo <-
      Implementation(Name = "migrondi-mcp", Version = McpVersion.get())

    options.ToolCollection <- buildToolCollection env serviceProvider readOnly
    options

  /// Transport-running part shared by the multi-project server and the
  /// single-project server: binds the mode's transport and runs the server.
  let runTransport
    (mode: McpMode)
    (serverOptions: McpServerOptions)
    (loggerFactory: ILoggerFactory)
    (serviceProvider: IServiceProvider)
    : Async<unit> = asyncEx {
    match mode with
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
        HttpServer.runHttpServer
          port
          serverOptions
          loggerFactory
          serviceProvider
          CancellationToken.None
  }

  let run
    (env: McpEnvironment)
    (options: McpOptions)
    (loggerFactory: ILoggerFactory)
    : Async<unit> = asyncEx {
    let services = ServiceCollection()

    services
      .AddSingleton<ILoggerFactory>(loggerFactory)
      .AddSingleton<McpEnvironment>(env)
    |> ignore

    let serviceProvider = services.BuildServiceProvider()

    let serverOptions = createServerOptions env serviceProvider options.readOnly

    do! runTransport options.mode serverOptions loggerFactory serviceProvider
  }
