namespace Migrondi.Mcp

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

open MigrondiUI
open Migrondi.Core
open Migrondi.Core.Serialization

open IcedTasks

// Delegates for the stateless single-project tools. `configPath` is the
// FIRST data parameter on every project tool: `Some path` targets that
// project, `None` falls back to the startup default, then the cwd chain.
// Parameter names come from these declarations, and the delegates are
// always constructed from lambdas (partial applications publish
// `delegateArg0` parameter names; verified against SDK 2.4.0).
type SingleProjectCallDelegate =
  delegate of
    configPath: string option * ct: CancellationToken -> Task<CallToolResult>
// get_project, list_migrations

type SingleAmountCallDelegate =
  delegate of
    configPath: string option * amount: int option * ct: CancellationToken ->
      Task<CallToolResult>
// dry_run_migrations, dry_run_rollback, run_migrations, run_rollback

type SingleCreateMigrationCallDelegate =
  delegate of
    configPath: string option *
    name: string *
    upContent: string option *
    downContent: string option *
    manualTransaction: bool option *
    ct: CancellationToken ->
      Task<CallToolResult>

type InitProjectDelegate =
  delegate of
    directory: string *
    driver: string option *
    connection: string option *
    migrations: string option *
    tableName: string option *
    force: bool option *
    ct: CancellationToken ->
      Task<CallToolResult>

type UpdateProjectConfigDelegate =
  delegate of
    configPath: string option *
    connection: string option *
    migrations: string option *
    tableName: string option *
    driver: string option *
    ct: CancellationToken ->
      Task<CallToolResult>

/// Startup-resolved defaults; every call layers its own configPath on top.
type ServerBinding = {
  Cwd: string // server startup working directory
  DefaultConfigPath: string option // --config, when given
  Overrides: ProjectOverrides // --connection/-m/-t/-d
}

type ToolContext = {
  Logger: ILogger
  Defaults: ServerBinding
}

/// Per-call routing: `Some path` targets that project; `None` falls back
/// to the startup default, then the cwd chain.
type ProjectCall = { ConfigPath: string option }

type CreateArgs = {
  Name: string
  Up: string option
  Down: string option
  ManualTransaction: bool option
}

type InitProjectArgs = {
  Directory: string // absolute, or relative to Defaults.Cwd
  Driver: string option // validated via MigrondiDriver.FromString
  Connection: string option
  Migrations: string option
  TableName: string option
  Force: bool option // AdoptExisting on non-empty directories
}

type UpdateConfigArgs = {
  Connection: string option
  Migrations: string option
  TableName: string option
  Driver: string option // validated via MigrondiDriver.FromString
}

module SingleProjectTools =

  /// Parses a raw driver value at the boundary into the typed driver;
  /// the error carries the invalid value for the tool's error result.
  let parseDriver(value: string) : Result<MigrondiDriver, string> =
    try
      Ok(MigrondiDriver.FromString value)
    with ex ->
      Error
        $"Invalid driver '{value}'. Valid options: sqlite, postgresql, mysql, mssql ({ex.Message})"

  /// Parses an optional raw driver value; `None` stays `None`.
  let parseDriverOption
    (value: string option)
    : Result<MigrondiDriver option, string> =
    value
    |> Option.map(parseDriver >> Result.map Some)
    |> Option.defaultValue(Ok None)

  /// Resolves the per-call binding: the call's explicit configPath wins,
  /// then the startup default, then the cwd chain. Re-resolved on every
  /// call, so config edits and env changes apply to the next call.
  let resolveCall
    (ctx: ToolContext)
    (call: ProjectCall)
    : Result<ProjectBinding, string> =
    let explicitConfig =
      match call.ConfigPath with
      | Some path -> Some path
      | None -> ctx.Defaults.DefaultConfigPath

    let resolveArgs: ProjectResolveArgs = {
      Cwd = ctx.Defaults.Cwd
      ExplicitConfig = explicitConfig
      Overrides = ctx.Defaults.Overrides
    }

    ProjectBinding.resolve ctx.Logger resolveArgs
    |> Result.mapError(fun error ->
      match error with
      | ConfigNotFound path -> $"Configuration file not found at '{path}'."
      | ConfigInvalid(path, reason) ->
        $"Invalid configuration file '{path}': {reason}")

  /// resolve -> factory -> Initialize -> op. Binding failures become
  /// McpResults.ErrorResult tool errors (message carries path/reason).
  let runWithProject
    (ctx: ToolContext)
    (call: ProjectCall)
    (op: IMigrondi -> CancellableTask<CallToolResult>)
    : CancellableTask<CallToolResult> = cancellableTask {

    match resolveCall ctx call with
    | Error message ->
      return
        McpResults.ErrorResult.Create message
        |> McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder
    | Ok binding ->
      let migrondi =
        Migrondi.MigrondiFactory(
          binding.Config,
          binding.Root,
          logger = ctx.Logger
        )

      // Per-call setup, idempotent (CREATE TABLE IF NOT EXISTS): the same
      // initialization every CLI invocation performs via withSetup.
      migrondi.Initialize()

      return! op migrondi
  }

  let bindingError(message: string) : CallToolResult =
    McpResults.ErrorResult.Create message
    |> McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder

  /// `MigrationsListAsync` encoded exactly like the multi-project
  /// list wrapper (`McpResults.ListMigrationsResult`).
  let listMigrations
    (ctx: ToolContext)
    (call: ProjectCall)
    : CancellableTask<CallToolResult> =
    runWithProject ctx call (fun migrondi -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      let! migrations = migrondi.MigrationsListAsync(ct)

      let result = {
        McpResults.ListMigrationsResult.migrations =
          migrations
          |> Seq.map McpResults.MigrationStatusOutput.FromMigrationStatus
          |> Seq.toArray
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder
    })

  let dryRunResult
    (migrations: Migration IReadOnlyList)
    : McpResults.DryRunResult =
    if migrations.Count = 0 then
      McpResults.DryRunResult.Empty
    else
      {
        McpResults.DryRunResult.count = migrations.Count
        migrations =
          migrations
          |> Seq.map McpResults.MigrationPreview.FromMigration
          |> Seq.toArray
      }

  /// `DryRunUpAsync` encoded exactly like the multi-project
  /// dry-run wrapper (`McpResults.DryRunResult`).
  let dryRunUp
    (ctx: ToolContext)
    (call: ProjectCall)
    (amount: int option)
    : CancellableTask<CallToolResult> =
    runWithProject ctx call (fun migrondi -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()

      let! migrations =
        migrondi.DryRunUpAsync(?amount = amount, cancellationToken = ct)

      return
        dryRunResult migrations
        |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
    })

  let dryRunDown
    (ctx: ToolContext)
    (call: ProjectCall)
    (amount: int option)
    : CancellableTask<CallToolResult> =
    runWithProject ctx call (fun migrondi -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()

      let! migrations =
        migrondi.DryRunDownAsync(?amount = amount, cancellationToken = ct)

      return
        dryRunResult migrations
        |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
    })

  /// `RunUpAsync` encoded exactly like the multi-project run wrapper
  /// (`McpResults.MigrationsResult`), with failures mapped to
  /// `MigrationsResult.Error` with the same messages.
  let runUp
    (ctx: ToolContext)
    (call: ProjectCall)
    (amount: int option)
    : CancellableTask<CallToolResult> =
    runWithProject ctx call (fun migrondi -> cancellableTask {
      try
        let! ct = CancellableTask.getCancellationToken()

        let! migrations =
          migrondi.RunUpAsync(?amount = amount, cancellationToken = ct)

        return
          migrations
          |> McpResults.MigrationsResult.FromMigrationRecords
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
      with ex ->
        return
          McpResults.MigrationsResult.Error
            $"Failed to apply migrations: {ex.Message}"
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
    })

  let runDown
    (ctx: ToolContext)
    (call: ProjectCall)
    (amount: int option)
    : CancellableTask<CallToolResult> =
    runWithProject ctx call (fun migrondi -> cancellableTask {
      try
        let! ct = CancellableTask.getCancellationToken()

        let! migrations =
          migrondi.RunDownAsync(?amount = amount, cancellationToken = ct)

        return
          migrations
          |> McpResults.MigrationsResult.FromMigrationRecords
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
      with ex ->
        return
          McpResults.MigrationsResult.Error
            $"Failed to rollback migrations: {ex.Message}"
          |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
    })

  /// `RunNewAsync` encoded exactly like the multi-project create wrapper
  /// (`McpResults.CreateMigrationResult`). Core's RunNew returns the
  /// FILENAME in `migration.name`, so the envelope derives both fields from
  /// the validated input: `name` is the friendly name and `fullName` is the
  /// real file name with a single timestamp.
  let createMigration
    (ctx: ToolContext)
    (call: ProjectCall)
    (args: CreateArgs)
    : CancellableTask<CallToolResult> =
    runWithProject ctx call (fun migrondi -> cancellableTask {
      match MigrationName.Validate args.Name with
      | Error errorMsg ->
        return
          McpResults.CreateMigrationResult.CreateMigrationError errorMsg
          |> McpResultMapper.fromEncoder
            McpResults.CreateMigrationResult.Encoder
      | Ok _ ->
        try
          let! ct = CancellableTask.getCancellationToken()

          let! migration =
            migrondi.RunNewAsync(
              args.Name,
              ?upContent = args.Up,
              ?downContent = args.Down,
              ?manualTransaction = args.ManualTransaction,
              cancellationToken = ct
            )

          return
            McpResults.CreateMigrationResult.MigrationCreated {|
              id = Guid.NewGuid()
              name = args.Name
              timestamp = migration.timestamp
              fullName = $"{migration.timestamp}_{args.Name}.sql"
            |}
            |> McpResultMapper.fromEncoder
              McpResults.CreateMigrationResult.Encoder
        with ex ->
          return
            McpResults.CreateMigrationResult.CreateMigrationError
              $"Failed to create migration: {ex.Message}"
            |> McpResultMapper.fromEncoder
              McpResults.CreateMigrationResult.Encoder
    })

  /// Reports the resolved per-call project surface (name, config path,
  /// driver, migrations folder, table name) and NEVER the connection
  /// string. Uses the binding directly: it needs Root and ConfigPath, which
  /// no `IMigrondi` exposes.
  let getProject
    (ctx: ToolContext)
    (call: ProjectCall)
    : CancellableTask<CallToolResult> = cancellableTask {
    match resolveCall ctx call with
    | Error message -> return bindingError message
    | Ok binding ->
      let separator = Path.DirectorySeparatorChar

      let name = Path.GetFileName(binding.Root.TrimEnd(separator))

      let migrationsFolder =
        Path.GetFullPath(binding.Config.migrations, binding.Root)

      let result: McpResults.ProjectInfoResult = {
        name = name
        configPath = binding.ConfigPath
        driver = binding.Config.driver.AsString
        migrationsFolder = migrationsFolder
        tableName = binding.Config.tableName
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.ProjectInfoResult.Encoder
  }

  /// The CLI's `--force --merge` recovery: only the field view of the file
  /// matters, so a missing or unparsable migrondi.json recovers to the
  /// defaults instead of failing the adopt.
  let recoverProjectConfig(configPath: string) : MigrondiConfig =
    let values =
      (try
        File.ReadAllText configPath
        |> fun content ->
          JsonSerializer.Deserialize<Map<string, string>>(content)
          |> Option.ofObj
       with _ ->
         None)
      |> Option.defaultValue Map.empty

    ProjectInit.recoverConfig values

  let initProjectError(message: string) : CallToolResult =
    McpResults.InitProjectResult.InitError message
    |> McpResultMapper.fromEncoder McpResults.InitProjectResult.Encoder

  let initializedResult(directory: string, mode: string) : CallToolResult =
    McpResults.InitProjectResult.ProjectInitialized {|
      directory = directory
      configPath = Path.Combine(directory, "migrondi.json")
      mode = mode
    |}
    |> McpResultMapper.fromEncoder McpResults.InitProjectResult.Encoder

  /// Creates a project in `args.Directory` (absolute, or relative to the
  /// server's startup cwd): empty -> defaults + overrides; non-empty with
  /// force -> recover the file's fields, rewrite, keep every file;
  /// non-empty without force -> typed refusal. Never deletes anything.
  let initProject
    (ctx: ToolContext)
    (args: InitProjectArgs)
    : CancellableTask<CallToolResult> = cancellableTask {
    match parseDriverOption args.Driver with
    | Error message -> return initProjectError message
    | Ok _ ->
      let directory =
        if
          Path.IsPathRooted args.Directory
          || Path.IsPathFullyQualified args.Directory
        then
          DirectoryInfo args.Directory
        else
          DirectoryInfo(Path.Combine(ctx.Defaults.Cwd, args.Directory))

      let isEmpty = ProjectInit.isDirectoryEmpty directory
      let force = defaultArg args.Force false

      let overrides: ProjectOverrides = {
        connection = args.Connection
        migrations = args.Migrations
        tableName = args.TableName
        driver = args.Driver
      }

      match ProjectInit.decide isEmpty force with
      | ProjectInit.InitDecision.RefuseNonEmpty ->
        return
          initProjectError
            $"The directory '{directory.FullName}' is not empty. Pass force=true to adopt its current values (files are never deleted); driver/connection/migrations/tableName override individual fields."
      | ProjectInit.InitDecision.CreateFresh ->
        let config = ProjectInit.applyOverrides MigrondiConfig.Default overrides

        match ProjectInit.writeProject directory config with
        | Error reason -> return initProjectError reason
        | Ok() -> return initializedResult(directory.FullName, "created")
      | ProjectInit.InitDecision.AdoptExisting ->
        let baseConfig =
          recoverProjectConfig(
            Path.Combine(directory.FullName, "migrondi.json")
          )

        let config = ProjectInit.applyOverrides baseConfig overrides

        match ProjectInit.writeProject directory config with
        | Error reason -> return initProjectError reason
        | Ok() -> return initializedResult(directory.FullName, "adopted")
  }

  let updateConfigError(message: string) : CallToolResult =
    McpResults.UpdateConfigResult.UpdateError message
    |> McpResultMapper.fromEncoder McpResults.UpdateConfigResult.Encoder

  let updatedConfigResult(configPath: string) : CallToolResult =
    McpResults.UpdateConfigResult.ConfigUpdated {| configPath = configPath |}
    |> McpResultMapper.fromEncoder McpResults.UpdateConfigResult.Encoder

  /// Re-writes the targeted project's migrondi.json with the given fields
  /// layered on top of the file's current values. Re-resolved per call, so
  /// the next call on the same configPath picks up the new values.
  let updateProjectConfig
    (ctx: ToolContext)
    (call: ProjectCall)
    (args: UpdateConfigArgs)
    : CancellableTask<CallToolResult> = cancellableTask {
    match parseDriverOption args.Driver with
    | Error message -> return updateConfigError message
    | Ok _ ->
      match resolveCall ctx call with
      | Error message -> return bindingError message
      | Ok binding ->
        let overrides: ProjectOverrides = {
          connection = args.Connection
          migrations = args.Migrations
          tableName = args.TableName
          driver = args.Driver
        }

        let config = ProjectInit.applyOverrides binding.Config overrides

        try
          File.WriteAllText(binding.ConfigPath, MiSerializer.Encode config)

          return updatedConfigResult binding.ConfigPath
        with ex ->
          return updateConfigError ex.Message
  }

module SingleProjectServer =

  module ToolSpecs =

    let readTool name title = {
      Name = name
      Title = title
      ReadOnly = true
      Destructive = false
      Idempotent = true
      OpenWorld = false
    }

    let writeTool name title destructive = {
      Name = name
      Title = title
      ReadOnly = false
      Destructive = destructive
      Idempotent = false
      OpenWorld = false
    }

  module Tools =

    let getProjectTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.readTool "get_project" "Get Project")
        (SingleProjectCallDelegate(fun configPath ct ->
          SingleProjectTools.getProject ctx { ConfigPath = configPath } ct))

    let listMigrationsTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.readTool "list_migrations" "List Migrations")
        (SingleProjectCallDelegate(fun configPath ct ->
          SingleProjectTools.listMigrations ctx { ConfigPath = configPath } ct))

    let dryRunMigrationsTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.readTool "dry_run_migrations" "Preview Migrations")
        (SingleAmountCallDelegate(fun configPath amount ct ->
          SingleProjectTools.dryRunUp ctx { ConfigPath = configPath } amount ct))

    let dryRunRollbackTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.readTool "dry_run_rollback" "Preview Rollback")
        (SingleAmountCallDelegate(fun configPath amount ct ->
          SingleProjectTools.dryRunDown
            ctx
            { ConfigPath = configPath }
            amount
            ct))

    let createMigrationTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool "create_migration" "Create Migration" false)
        (SingleCreateMigrationCallDelegate
          (fun configPath name upContent downContent manualTransaction ct ->
            SingleProjectTools.createMigration
              ctx
              { ConfigPath = configPath }
              {
                Name = name
                Up = upContent
                Down = downContent
                ManualTransaction = manualTransaction
              }
              ct))

    let runMigrationsTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool "run_migrations" "Apply Migrations" true)
        (SingleAmountCallDelegate(fun configPath amount ct ->
          SingleProjectTools.runUp ctx { ConfigPath = configPath } amount ct))

    let runRollbackTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool "run_rollback" "Rollback Migrations" true)
        (SingleAmountCallDelegate(fun configPath amount ct ->
          SingleProjectTools.runDown ctx { ConfigPath = configPath } amount ct))

    let initProjectTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool "init_project" "Init Project" false)
        (InitProjectDelegate
          (fun directory driver connection migrations tableName force ct ->
            SingleProjectTools.initProject
              ctx
              {
                Directory = directory
                Driver = driver
                Connection = connection
                Migrations = migrations
                TableName = tableName
                Force = force
              }
              ct))

    let updateProjectConfigTool
      (ctx: ToolContext)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool
          "update_project_config"
          "Update Project Config"
          false)
        (UpdateProjectConfigDelegate
          (fun configPath connection migrations tableName driver ct ->
            SingleProjectTools.updateProjectConfig
              ctx
              { ConfigPath = configPath }
              {
                Connection = connection
                Migrations = migrations
                TableName = tableName
                Driver = driver
              }
              ct))

  /// Serving parameters shared by `buildServerOptions` and `run`.
  /// `Mode` is consumed by `run` only; `buildServerOptions` ignores it.
  type ServeArgs = {
    Mode: McpMode
    ReadOnly: bool
    ExtraTools: McpServerTool list
  }

  /// Builds server options for the stateless surface; `ExtraTools` lets a
  /// host add binding-owned tools.
  let buildServerOptions
    (ctx: ToolContext)
    (serviceProvider: IServiceProvider)
    (args: ServeArgs)
    : McpServerOptions =
    let options =
      McpServerOptions(
        ServerInfo =
          Implementation(Name = "migrondi-mcp", Version = McpVersion.get())
      )

    let collection = McpServerPrimitiveCollection<McpServerTool>()

    for tool in
      [
        Tools.getProjectTool ctx serviceProvider
        Tools.listMigrationsTool ctx serviceProvider
        Tools.dryRunMigrationsTool ctx serviceProvider
        Tools.dryRunRollbackTool ctx serviceProvider
      ] do
      collection.Add(tool)

    if not args.ReadOnly then
      for tool in
        [
          Tools.createMigrationTool ctx serviceProvider
          Tools.runMigrationsTool ctx serviceProvider
          Tools.runRollbackTool ctx serviceProvider
          Tools.initProjectTool ctx serviceProvider
          Tools.updateProjectConfigTool ctx serviceProvider
        ] do
        collection.Add(tool)

    for tool in args.ExtraTools do
      collection.Add(tool)

    options.ToolCollection <- collection
    options

  let run
    (ctx: ToolContext)
    (loggerFactory: ILoggerFactory)
    (args: ServeArgs)
    : Async<unit> = asyncEx {
    let services = ServiceCollection()

    let serviceProvider =
      services
        .AddSingleton<ILoggerFactory>(loggerFactory)
        .AddSingleton<ToolContext>(ctx)
        .BuildServiceProvider()

    let serverOptions = buildServerOptions ctx serviceProvider args

    do!
      ServerBuilder.runTransport
        args.Mode
        serverOptions
        loggerFactory
        serviceProvider
  }
