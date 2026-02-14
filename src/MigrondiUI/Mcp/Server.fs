namespace MigrondiUI.Mcp

open System
open System.Collections.Concurrent
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
          match Int32.TryParse(argv.[i + 1]) with
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

    let lProjects, vProjects = Projects.GetRepositories connectionFactory

    let vMigrondiFactory = MigrondiExt.getMigrondiUI(loggerFactory, vProjects)

    let localMigrondiFactory(config: MigrondiConfig, rootDir: string) =
      let mLogger: ILogger = loggerFactory.CreateLogger<IMigrondi>()

      let migrondi: IMigrondi =
        Migrondi.Core.Migrondi.MigrondiFactory(config, rootDir, mLogger)

      MigrondiExt.wrapLocalMigrondi(migrondi, config, rootDir)

    let vfs =
      let logger = loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>()
      VirtualFs.getVirtualFs(logger, vProjects)

    {
      lf = loggerFactory
      lProjects = lProjects
      vProjects = vProjects
      vfs = vfs
      vMigrondiFactory = vMigrondiFactory
      localMigrondiFactory = localMigrondiFactory
      migrondiCache = ConcurrentDictionary<Guid, MigrondiExt.IMigrondiUI>()
    }

  let createTool
    (serviceProvider: IServiceProvider)
    name
    title
    (readOnly: bool)
    (destructive: bool)
    (del: Delegate)
    : McpServerTool =
    let options =
      McpServerToolCreateOptions(
        Services = serviceProvider,
        Name = name,
        Title = title,
        ReadOnly = readOnly,
        Destructive = destructive
      )

    McpServerTool.Create(del, options)

  let createReadTools
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    : McpServerTool list =

    let listProjectsFn ct = McpTools.listProjects env ct
    let getProjectFn pid ct = McpTools.getProject env pid ct
    let listMigrationsFn pid ct = McpTools.listMigrations env pid ct

    let getMigrationFn (guid: string) name ct =
      match Guid.TryParse guid with
      | false, _ -> McpTools.getMigration env System.Guid.Empty name ct
      | true, guid -> McpTools.getMigration env guid name ct

    let dryRunMigrationsFn pid amount ct =
      McpTools.dryRunMigrations env pid amount ct

    let dryRunRollbackFn pid amount ct =
      McpTools.dryRunRollback env pid amount ct

    [
      createTool
        serviceProvider
        "list_projects"
        "List Projects"
        true
        false
        (ListProjectsDelegate listProjectsFn)
      createTool
        serviceProvider
        "get_project"
        "Get Project"
        true
        false
        (GetProjectDelegate getProjectFn)
      createTool
        serviceProvider
        "list_migrations"
        "List Migrations"
        true
        false
        (ListMigrationsDelegate listMigrationsFn)
      createTool
        serviceProvider
        "get_migration"
        "Get Migration"
        true
        false
        (GetMigrationDelegate getMigrationFn)
      createTool
        serviceProvider
        "dry_run_migrations"
        "Preview Migrations"
        true
        false
        (DryRunMigrationsDelegate dryRunMigrationsFn)
      createTool
        serviceProvider
        "dry_run_rollback"
        "Preview Rollback"
        true
        false
        (DryRunMigrationsDelegate dryRunRollbackFn)
    ]

  let createWriteTools
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    : McpServerTool list =

    let runMigrationsFn pid amount ct =
      McpWriteTools.runMigrations env pid amount ct

    let runRollbackFn pid amount ct =
      McpWriteTools.runRollback env pid amount ct

    let createMigrationFn pid name up down ct =
      McpWriteTools.createMigration env pid name up down ct

    let updateMigrationFn (projectId: string) name up down ct =
      match Guid.TryParse projectId with
      | false, _ ->
        McpWriteTools.updateMigration env System.Guid.Empty name up down ct
      | true, guid -> McpWriteTools.updateMigration env guid name up down ct

    let deleteMigrationFn (projectId: string) name ct =
      match Guid.TryParse projectId with
      | false, _ -> McpWriteTools.deleteMigration env System.Guid.Empty name ct
      | true, guid -> McpWriteTools.deleteMigration env guid name ct

    let createVirtualProjectFn name conn driver desc tbl ct =
      McpWriteTools.createVirtualProject env name conn driver desc tbl ct

    let updateVirtualProjectFn pid name conn tbl driver ct =
      McpWriteTools.updateVirtualProject env pid name conn tbl driver ct

    let deleteProjectFn pid ct = McpWriteTools.deleteProject env pid ct

    let exportVirtualProjectFn pid path ct =
      McpWriteTools.exportVirtualProject env pid path ct

    let importFromLocalFn path ct =
      McpWriteTools.importFromLocal env path ct

    [
      createTool
        serviceProvider
        "run_migrations"
        "Apply Migrations"
        false
        true
        (RunMigrationsDelegate runMigrationsFn)
      createTool
        serviceProvider
        "run_rollback"
        "Rollback Migrations"
        false
        true
        (RunMigrationsDelegate runRollbackFn)
      createTool
        serviceProvider
        "create_migration"
        "Create Migration"
        false
        false
        (CreateMigrationDelegate createMigrationFn)
      createTool
        serviceProvider
        "update_migration"
        "Update Migration"
        false
        false
        (UpdateMigrationDelegate updateMigrationFn)
      createTool
        serviceProvider
        "delete_migration"
        "Delete Migration"
        false
        true
        (DeleteMigrationDelegate deleteMigrationFn)
      createTool
        serviceProvider
        "create_virtual_project"
        "Create Virtual Project"
        false
        false
        (CreateVirtualProjectDelegate createVirtualProjectFn)
      createTool
        serviceProvider
        "update_virtual_project"
        "Update Virtual Project"
        false
        false
        (UpdateVirtualProjectDelegate updateVirtualProjectFn)
      createTool
        serviceProvider
        "delete_project"
        "Delete Project"
        false
        true
        (DeleteProjectDelegate deleteProjectFn)
      createTool
        serviceProvider
        "export_virtual_project"
        "Export Virtual Project"
        false
        false
        (ExportVirtualProjectDelegate exportVirtualProjectFn)
      createTool
        serviceProvider
        "import_from_local"
        "Import from Local"
        false
        false
        (ImportFromLocalDelegate importFromLocalFn)
    ]

  let buildToolCollection
    (env: McpEnvironment)
    (serviceProvider: IServiceProvider)
    (readOnly: bool)
    : McpServerPrimitiveCollection<McpServerTool> =
    let collection = McpServerPrimitiveCollection<McpServerTool>()

    for tool in createReadTools env serviceProvider do
      collection.Add(tool) |> ignore

    if not readOnly then
      for tool in createWriteTools env serviceProvider do
        collection.Add(tool) |> ignore

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

  open ModelContextProtocol.Protocol

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
