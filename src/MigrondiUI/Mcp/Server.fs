namespace MigrondiUI.Mcp

open System
open System.Threading

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

open ModelContextProtocol.Server

open MigrondiUI

open IcedTasks

type IMcpServer =
  abstract Run: unit -> Async<unit>

module private ServerHelpers =

  module ReadTools =
    let listProjectsFn env ct = McpTools.listProjects env ct

    let getProjectFn env (pid: string) ct =
      match Guid.TryParse pid with
      | false, _ -> McpTools.getProject env Guid.Empty ct
      | true, guid -> McpTools.getProject env guid ct

    let listMigrationsFn env (pid: string) ct =
      match Guid.TryParse pid with
      | false, _ -> McpTools.listMigrations env Guid.Empty ct
      | true, guid -> McpTools.listMigrations env guid ct

    let getMigrationFn env (guid: string) name ct =
      match Guid.TryParse guid with
      | false, _ -> McpTools.getMigration env Guid.Empty name ct
      | true, guid -> McpTools.getMigration env guid name ct

    let dryRunMigrationsFn env (pid: string) amount ct =
      match Guid.TryParse pid with
      | false, _ -> McpTools.dryRunMigrations env Guid.Empty amount ct
      | true, guid -> McpTools.dryRunMigrations env guid amount ct

    let dryRunRollbackFn env (pid: string) amount ct =
      match Guid.TryParse pid with
      | false, _ -> McpTools.dryRunRollback env Guid.Empty amount ct
      | true, guid -> McpTools.dryRunRollback env guid amount ct

  module WriteTools =
    let runMigrationsFn env pid amount ct =
      McpTools.runMigrations env pid amount ct

    let runRollbackFn env pid amount ct = McpTools.runRollback env pid amount ct

    let createMigrationFn env pid name up down ct =
      McpTools.createMigration env pid name up down ct

    let updateMigrationFn env (projectId: string) name up down ct =
      match Guid.TryParse projectId with
      | false, _ -> McpTools.updateMigration env Guid.Empty name up down ct
      | true, guid -> McpTools.updateMigration env guid name up down ct

    let deleteMigrationFn env (projectId: string) name ct =
      match Guid.TryParse projectId with
      | false, _ -> McpTools.deleteMigration env Guid.Empty name ct
      | true, guid -> McpTools.deleteMigration env guid name ct

    let createVirtualProjectFn env name conn driver desc tbl ct =
      McpTools.createVirtualProject env name conn driver desc tbl ct

    let updateVirtualProjectFn env pid name conn tbl driver ct =
      McpTools.updateVirtualProject env pid name conn tbl driver ct

    let deleteProjectFn env pid ct = McpTools.deleteProject env pid ct

    let exportVirtualProjectFn env pid path ct =
      McpTools.exportVirtualProject env pid path ct

    let importFromLocalFn env path ct = McpTools.importFromLocal env path ct

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
