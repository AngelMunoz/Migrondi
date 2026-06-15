namespace MigrondiUI.Mcp

open System
open System.Threading

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

open ModelContextProtocol.Server

open MigrondiUI
open Migrondi.Core

open IcedTasks

type IMcpServer =
  abstract Run: unit -> Async<unit>

module private ServerHelpers =

  module ReadTools =
    let listProjectsFn env ct = task {
      let! projects = McpTools.listProjects env ct

      let result: McpResults.ListProjectsResult = {
        local =
          projects
          |> List.choose (function
            | Local p -> Some(McpResults.LocalProjectSummary.FromLocalProject p)
            | Virtual _ -> None)
        virtualProjects =
          projects
          |> List.choose (function
            | Virtual p ->
              Some(McpResults.VirtualProjectSummary.FromVirtualProject p)
            | Local _ -> None)
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.ListProjectsResult.Encoder
    }

    let getProjectFn env (pid: string) ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.GetProjectResult.ProjectNotFound
            $"Invalid project ID: {pid}"
          |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder
      | true, guid ->
        let! project = McpTools.getProject env guid ct

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

    let listMigrationsFn env (pid: string) ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.ListMigrationsResult.Empty
          |> McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder
      | true, guid ->
        let! migrations = McpTools.listMigrations env guid ct

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

    let getMigrationFn env (guid: string) name ct = task {
      match Guid.TryParse guid with
      | false, _ ->
        return
          McpResults.GetMigrationResult.MigrationNotFound "Invalid project ID"
          |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
      | true, projectId ->
        let! result = McpTools.getMigration env projectId name ct

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

    let dryRunMigrationsFn env (pid: string) amount ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.DryRunResult.Empty
          |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
      | true, guid ->
        let! migrations = McpTools.dryRunMigrations env guid amount ct

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

    let dryRunRollbackFn env (pid: string) amount ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.DryRunResult.Empty
          |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
      | true, guid ->
        let! migrations = McpTools.dryRunRollback env guid amount ct

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
    let runMigrationsFn env (pid: string) amount ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.MigrationsResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
      | true, guid ->
        let! result = McpTools.runMigrations env guid amount ct

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

    let runRollbackFn env (pid: string) amount ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.MigrationsResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
      | true, guid ->
        let! result = McpTools.runRollback env guid amount ct

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

    let createMigrationFn env (pid: string) name up down ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.CreateMigrationResult.CreateMigrationError
            "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder
            McpResults.CreateMigrationResult.Encoder
      | true, guid ->
        let! result = McpTools.createMigration env guid name up down ct

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

    let updateMigrationFn env (projectId: string) name up down ct = task {
      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResults.SuccessResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
      | true, guid ->
        let! result = McpTools.updateMigration env guid name up down ct

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

    let deleteMigrationFn env (projectId: string) name ct = task {
      match Guid.TryParse projectId with
      | false, _ ->
        return
          McpResults.SuccessResult.Error "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
      | true, guid ->
        let! result = McpTools.deleteMigration env guid name ct

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

    let createVirtualProjectFn env name conn driver desc tbl ct = task {
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
          McpTools.createVirtualProject env name conn driver desc tbl ct

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

    let updateVirtualProjectFn env (pid: string) name conn tbl driver ct = task {
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
          McpTools.updateVirtualProject env guid name conn tbl driverValue ct

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

    let deleteProjectFn env (pid: string) ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.ErrorResult.Create "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder
      | true, guid ->
        let! result = McpTools.deleteProject env guid ct

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

    let exportVirtualProjectFn env (pid: string) path ct = task {
      match Guid.TryParse pid with
      | false, _ ->
        return
          McpResults.ExportResult.ExportError "projectId must be a valid GUID"
          |> McpResultMapper.fromEncoder McpResults.ExportResult.Encoder
      | true, guid ->
        let! result = McpTools.exportVirtualProject env guid path ct

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

    let importFromLocalFn env path ct = task {
      let! result = McpTools.importFromLocal env path ct

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

  let createEnvironment
    (connectionFactory: unit -> System.Data.IDbConnection)
    (loggerFactory: ILoggerFactory)
    : McpEnvironment =
    Migrations.GetMigrondi loggerFactory
    |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
    |> Migrations.Migrate

    let projects =
      Services.ProjectCollection(
        loggerFactory.CreateLogger(),
        connectionFactory
      )

    let migrondiFactory =
      Services.MigrationOperationsFactory(loggerFactory, connectionFactory)

    {
      lf = loggerFactory
      projects = projects
      migrondiFactory = migrondiFactory
    }

  let createTool
    (serviceProvider: IServiceProvider)
    name
    title
    (readOnly: bool)
    (destructive: bool)
    (del: Delegate)
    : McpServerTool =

    McpServerTool.Create(
      del,
      McpServerToolCreateOptions(
        Services = serviceProvider,
        Name = name,
        Title = title,
        ReadOnly = readOnly,
        Destructive = destructive
      )
    )

  let createReadTools
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    : McpServerTool list =
    [
      createTool
        serviceProvider
        "list_projects"
        "List Projects"
        true
        false
        (ListProjectsDelegate(ReadTools.listProjectsFn env))
      createTool
        serviceProvider
        "get_project"
        "Get Project"
        true
        false
        (GetProjectDelegate(ReadTools.getProjectFn env))
      createTool
        serviceProvider
        "list_migrations"
        "List Migrations"
        true
        false
        (ListMigrationsDelegate(ReadTools.listMigrationsFn env))
      createTool
        serviceProvider
        "get_migration"
        "Get Migration"
        true
        false
        (GetMigrationDelegate(ReadTools.getMigrationFn env))
      createTool
        serviceProvider
        "dry_run_migrations"
        "Preview Migrations"
        true
        false
        (DryRunMigrationsDelegate(ReadTools.dryRunMigrationsFn env))
      createTool
        serviceProvider
        "dry_run_rollback"
        "Preview Rollback"
        true
        false
        (DryRunMigrationsDelegate(ReadTools.dryRunRollbackFn env))
    ]

  let createWriteTools
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    : McpServerTool list =
    [
      createTool
        serviceProvider
        "run_migrations"
        "Apply Migrations"
        false
        true
        (RunMigrationsDelegate(WriteTools.runMigrationsFn env))
      createTool
        serviceProvider
        "run_rollback"
        "Rollback Migrations"
        false
        true
        (RunMigrationsDelegate(WriteTools.runRollbackFn env))
      createTool
        serviceProvider
        "create_migration"
        "Create Migration"
        false
        false
        (CreateMigrationDelegate(WriteTools.createMigrationFn env))
      createTool
        serviceProvider
        "update_migration"
        "Update Migration"
        false
        false
        (UpdateMigrationDelegate(WriteTools.updateMigrationFn env))
      createTool
        serviceProvider
        "delete_migration"
        "Delete Migration"
        false
        true
        (DeleteMigrationDelegate(WriteTools.deleteMigrationFn env))
      createTool
        serviceProvider
        "create_virtual_project"
        "Create Virtual Project"
        false
        false
        (CreateVirtualProjectDelegate(WriteTools.createVirtualProjectFn env))
      createTool
        serviceProvider
        "update_virtual_project"
        "Update Virtual Project"
        false
        false
        (UpdateVirtualProjectDelegate(WriteTools.updateVirtualProjectFn env))
      createTool
        serviceProvider
        "delete_project"
        "Delete Project"
        false
        true
        (DeleteProjectDelegate(WriteTools.deleteProjectFn env))
      createTool
        serviceProvider
        "export_virtual_project"
        "Export Virtual Project"
        false
        false
        (ExportVirtualProjectDelegate(WriteTools.exportVirtualProjectFn env))
      createTool
        serviceProvider
        "import_from_local"
        "Import from Local"
        false
        false
        (ImportFromLocalDelegate(WriteTools.importFromLocalFn env))
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
      Implementation(Name = "migrondi-mcp", Version = "1.2.0")

    options.ToolCollection <- buildToolCollection env serviceProvider readOnly
    options

module Server =

  let tryParseArgs = ServerHelpers.parseArgs

  let runMcpServer
    (connectionFactory: unit -> System.Data.IDbConnection)
    (options: McpOptions)
    (loggerFactory: ILoggerFactory)
    : Async<unit> =
    asyncEx {
      let env = ServerHelpers.createEnvironment connectionFactory loggerFactory

      let services = ServiceCollection()
      services.AddSingleton<ILoggerFactory>(loggerFactory) |> ignore
      services.AddSingleton<McpEnvironment>(env) |> ignore

      let serviceProvider = services.BuildServiceProvider()

      let serverOptions =
        ServerHelpers.createServerOptions env serviceProvider options.readOnly

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
          HttpServer.runHttpServer
            port
            serverOptions
            loggerFactory
            serviceProvider
            CancellationToken.None
    }

  let create
    (connectionFactory: unit -> System.Data.IDbConnection)
    (options: McpOptions)
    (loggerFactory: ILoggerFactory)
    : IMcpServer =

    { new IMcpServer with
        member _.Run() : Async<unit> =
          runMcpServer connectionFactory options loggerFactory
    }
