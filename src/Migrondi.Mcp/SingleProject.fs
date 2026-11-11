namespace Migrondi.Mcp

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

open ModelContextProtocol.Protocol
open ModelContextProtocol.Server

open MigrondiUI
open Migrondi.Core

open IcedTasks

// Delegates for the single-project tools: like the multi-project delegates
// but WITHOUT projectId — the server serves exactly one project. Parameter
// names come from these declarations, and the delegates are constructed
// directly from the F# tool functions.
type SingleListMigrationsDelegate =
  delegate of ct: CancellationToken -> Task<CallToolResult>

type SingleAmountDelegate =
  delegate of amount: int option * ct: CancellationToken -> Task<CallToolResult>

type SingleCreateMigrationDelegate =
  delegate of
    name: string *
    upContent: string option *
    downContent: string option *
    ct: CancellationToken ->
      Task<CallToolResult>

/// The tool layer works on `IMigrondi` directly (Key Decision: it knows
/// nothing about folders or project registries).
type SingleProjectEnv = {
  lf: ILoggerFactory
  migrondi: IMigrondi
}

type CreateArgs = {
  Name: string
  Up: string option
  Down: string option
}

module SingleProjectTools =

  /// `MigrationsListAsync` encoded exactly like the multi-project
  /// list wrapper (`McpResults.ListMigrationsResult`).
  let listMigrations(env: SingleProjectEnv) : CancellableTask<CallToolResult> =
    cancellableTask {
      let! migrations = env.migrondi.MigrationsListAsync

      let result = {
        McpResults.ListMigrationsResult.migrations =
          migrations
          |> Seq.map McpResults.MigrationStatusOutput.FromMigrationStatus
          |> Seq.toArray
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder
    }

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
    (env: SingleProjectEnv)
    (amount: int option)
    : CancellableTask<CallToolResult> = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()

    let! migrations =
      env.migrondi.DryRunUpAsync(?amount = amount, cancellationToken = ct)

    return
      dryRunResult migrations
      |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
  }

  let dryRunDown
    (env: SingleProjectEnv)
    (amount: int option)
    : CancellableTask<CallToolResult> = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()

    let! migrations =
      env.migrondi.DryRunDownAsync(?amount = amount, cancellationToken = ct)

    return
      dryRunResult migrations
      |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
  }

  /// `RunUpAsync` encoded exactly like the multi-project run wrapper
  /// (`McpResults.MigrationsResult`), with failures mapped to
  /// `MigrationsResult.Error` with the same messages.
  let runUp
    (env: SingleProjectEnv)
    (amount: int option)

    : CancellableTask<CallToolResult> = cancellableTask {
    try
      let! ct = CancellableTask.getCancellationToken()

      let! migrations =
        env.migrondi.RunUpAsync(?amount = amount, cancellationToken = ct)

      return
        migrations
        |> McpResults.MigrationsResult.FromMigrationRecords
        |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
    with ex ->
      return
        McpResults.MigrationsResult.Error
          $"Failed to apply migrations: {ex.Message}"
        |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
  }

  let runDown
    (env: SingleProjectEnv)
    (amount: int option)

    : CancellableTask<CallToolResult> = cancellableTask {
    try
      let! ct = CancellableTask.getCancellationToken()

      let! migrations =
        env.migrondi.RunDownAsync(?amount = amount, cancellationToken = ct)

      return
        migrations
        |> McpResults.MigrationsResult.FromMigrationRecords
        |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
    with ex ->
      return
        McpResults.MigrationsResult.Error
          $"Failed to rollback migrations: {ex.Message}"
        |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
  }

  /// `RunNewAsync` encoded exactly like the multi-project create wrapper
  /// (`McpResults.CreateMigrationResult`). Core's RunNew returns the
  /// FILENAME in `migration.name`, so the envelope derives both fields from
  /// the validated input: `name` is the friendly name and `fullName` is the
  /// real file name with a single timestamp.
  let createMigration
    (env: SingleProjectEnv)
    (args: CreateArgs)
    : CancellableTask<CallToolResult> = cancellableTask {
    match MigrationName.Validate args.Name with
    | Error errorMsg ->
      return
        McpResults.CreateMigrationResult.CreateMigrationError errorMsg
        |> McpResultMapper.fromEncoder McpResults.CreateMigrationResult.Encoder
    | Ok _ ->
      try
        let! ct = CancellableTask.getCancellationToken()

        let! migration =
          env.migrondi.RunNewAsync(
            args.Name,
            ?upContent = args.Up,
            ?downContent = args.Down,
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

    let listMigrationsTool
      (env: SingleProjectEnv)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.readTool "list_migrations" "List Migrations")
        (SingleListMigrationsDelegate(fun ct ->
          SingleProjectTools.listMigrations env ct))

    let dryRunMigrationsTool
      (env: SingleProjectEnv)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.readTool "dry_run_migrations" "Preview Migrations")
        // Partial applications (dryRunUp env) degrade the schema parameter
        // names to delegateArg0; a lambda keeps the delegate's declared
        // names (verified against SDK 2.4.0).
        (SingleAmountDelegate(fun amount ct ->
          SingleProjectTools.dryRunUp env amount ct))

    let dryRunRollbackTool
      (env: SingleProjectEnv)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.readTool "dry_run_rollback" "Preview Rollback")
        (SingleAmountDelegate(fun amount ct ->
          SingleProjectTools.dryRunDown env amount ct))

    let createMigrationTool
      (env: SingleProjectEnv)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool "create_migration" "Create Migration" false)
        (SingleCreateMigrationDelegate(fun name upContent downContent ct ->
          SingleProjectTools.createMigration
            env
            {
              Name = name
              Up = upContent
              Down = downContent
            }
            ct))

    let runMigrationsTool
      (env: SingleProjectEnv)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool "run_migrations" "Apply Migrations" true)
        (SingleAmountDelegate(fun amount ct ->
          SingleProjectTools.runUp env amount ct))

    let runRollbackTool
      (env: SingleProjectEnv)
      (serviceProvider: IServiceProvider)
      : McpServerTool =
      ServerBuilder.createTool
        serviceProvider
        (ToolSpecs.writeTool "run_rollback" "Rollback Migrations" true)
        (SingleAmountDelegate(fun amount ct ->
          SingleProjectTools.runDown env amount ct))

  /// Serving parameters shared by `buildServerOptions` and `run`.
  /// `Mode` is consumed by `run` only; `buildServerOptions` ignores it.
  type ServeArgs = {
    Mode: McpMode
    ReadOnly: bool
    ExtraTools: McpServerTool list
  }

  /// Builds server options for one project; `ExtraTools` lets a host add
  /// binding-owned tools (e.g. the CLI's project-info tool).
  let buildServerOptions
    (env: SingleProjectEnv)
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
        Tools.listMigrationsTool env serviceProvider
        Tools.dryRunMigrationsTool env serviceProvider
        Tools.dryRunRollbackTool env serviceProvider
      ] do
      collection.Add(tool)

    if not args.ReadOnly then
      for tool in
        [
          Tools.createMigrationTool env serviceProvider
          Tools.runMigrationsTool env serviceProvider
          Tools.runRollbackTool env serviceProvider
        ] do
        collection.Add(tool)

    for tool in args.ExtraTools do
      collection.Add(tool)

    options.ToolCollection <- collection
    options

  let run
    (env: SingleProjectEnv)
    (loggerFactory: ILoggerFactory)
    (args: ServeArgs)
    : Async<unit> = asyncEx {
    let services = ServiceCollection()

    let serviceProvider =
      services
        .AddSingleton<ILoggerFactory>(loggerFactory)
        .AddSingleton<SingleProjectEnv>(env)
        .BuildServiceProvider()

    let serverOptions = buildServerOptions env serviceProvider args

    do!
      ServerBuilder.runTransport
        args.Mode
        serverOptions
        loggerFactory
        serviceProvider
  }
