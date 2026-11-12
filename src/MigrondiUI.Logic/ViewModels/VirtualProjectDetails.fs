namespace MigrondiUI.Logic

module VirtualProjectDetails =

  open System
  open System.Threading.Tasks

  open Microsoft.Extensions.Logging

  open FSharp.Data.Adaptive

  open IcedTasks
  open IcedTasks.Polyfill.Async.PolyfillBuilders

  open Migrondi.Core
  open MigrondiUI
  open MigrondiUI.Services

  /// Data-only snapshot of the virtual project details state.
  [<NoComparison; NoEquality>]
  type VirtualProjectDetailsState = {
    Migrations: MigrationStatus[]
    LastDryRun: Migration[]
    CurrentShow: MigrationRunner.CurrentShow
  }

  /// When a command's Async completes, all state writes are complete; a host
  /// may rebuild its view immediately after.
  type VirtualProjectDetailsVM
    (
      logger: ILogger<VirtualProjectDetailsVM>,
      projects: IProjectCollection,
      migrondiFactory: IMigrationOperationsFactory,
      project: VirtualProject,
      navigator: INavigator
    ) =

    let _migrations = cval [||]
    let lastDryRun = cval [||]
    let currentShow = cval MigrationRunner.CurrentShow.Migrations

    let _project = cval project

    let ops = migrondiFactory.Create(Virtual project)

    let handleError(work: Async<unit>) = asyncEx {
      let! result = work |> Async.Catch

      match result with
      | Choice1Of2 _ -> return ()
      | Choice2Of2 ex ->
        logger.LogError("An error occurred: {error}", ex)
        currentShow.setValue(MigrationRunner.CurrentShow.ExceptionThrown ex)
        return ()
    }

    do
      logger.LogDebug "VirtualProjectDetailsVM created"
      logger.LogDebug "Initializing migrondi"
      logger.LogDebug("Project connection: {project}", project.connection)
      ops.Core.Initialize()

    member _.Project: VirtualProject aval = _project

    member _.Migrations: MigrationStatus[] aval = _migrations

    member _.LastDryRun: Migration[] aval = lastDryRun

    member _.CurrentShow: MigrationRunner.CurrentShow aval = currentShow

    member _.State: VirtualProjectDetailsState aval =
      AVal.map3
        (fun migrations lastDryRun currentShow -> {
          Migrations = migrations
          LastDryRun = lastDryRun
          CurrentShow = currentShow
        })
        _migrations
        lastDryRun
        currentShow

    member _.ListMigrations() = asyncEx {
      logger.LogDebug "Listing migrations"

      // token edge: defaults to None internally
      let! ct = Async.CancellationToken
      let! migrations = ops.Core.MigrationsListAsync ct

      logger.LogDebug("Migrations listed: {migrations}", migrations.Count)

      migrations |> Seq.toArray |> _migrations.setValue

      lastDryRun.setValue [||]
      logger.LogDebug "Last dry run set to empty array"

      currentShow.setValue MigrationRunner.CurrentShow.Migrations
      return ()
    }

    member this.CreateNewMigration(name: string) =
      asyncEx {
        logger.LogDebug("Creating new migration with name: {name}", name)

        // token edge: defaults to None internally
        let! ct = Async.CancellationToken
        do! ops.Core.RunNewAsync(name, cancellationToken = ct) :> Task
        return! this.ListMigrations()
      }
      |> handleError

    member _.SaveMigration(migration: Migration) = asyncEx {
      logger.LogDebug("Saving migration: {migration}", migration.name)

      let! result = ops.UpdateMigration migration

      match result with
      | Ok _ ->
        logger.LogDebug("Migration saved: {migration}", migration.name)
        return true
      | Error(Services.MigrationCrudError.AlreadyApplied n) ->
        logger.LogWarning("Migration {name} has already been applied", n)
        return false
      | Error(Services.MigrationCrudError.NotFound n) ->
        logger.LogWarning("Migration {name} not found", n)
        return false
      | Error(Services.MigrationCrudError.DatabaseError msg) ->
        logger.LogError("Unable to save the migration: {error}", msg)
        return false
    }

    member _.DeleteMigration(migration: Migration) = asyncEx {
      logger.LogDebug("Deleting migration: {migration}", migration.name)

      let! result = ops.DeleteMigration migration.name

      match result with
      | Ok _ ->
        logger.LogDebug("Migration deleted: {migration}", migration.name)
        return true
      | Error(Services.MigrationCrudError.AlreadyApplied n) ->
        logger.LogWarning("Migration {name} has already been applied", n)
        return false
      | Error(Services.MigrationCrudError.NotFound n) ->
        logger.LogWarning("Migration {name} not found", n)
        return false
      | Error(Services.MigrationCrudError.DatabaseError msg) ->
        logger.LogError("Unable to delete the migration: {error}", msg)
        return false
    }

    member this.RunMigrations
      (kind: MigrationRunner.RunMigrationKind, steps: int)
      =
      asyncEx {
        // token edge: defaults to None internally
        let! token = Async.CancellationToken
        logger.LogDebug("Running migrations: {kind}, {steps}", kind, steps)

        match kind with
        | MigrationRunner.RunMigrationKind.Up ->
          logger.LogDebug("Running migrations up")
          do! ops.Core.RunUpAsync(steps, cancellationToken = token) :> Task
          do! this.ListMigrations()
          logger.LogDebug("Migrations up completed")
          return ()
        | MigrationRunner.RunMigrationKind.Down ->
          logger.LogDebug("Running migrations down")
          do! ops.Core.RunDownAsync(steps, cancellationToken = token) :> Task
          do! this.ListMigrations()
          logger.LogDebug("Migrations down completed")
          return ()
        | MigrationRunner.RunMigrationKind.DryUp ->
          logger.LogDebug("Running migrations dry up")
          let! run = ops.Core.DryRunUpAsync(steps, cancellationToken = token)

          logger.LogDebug(
            "Dry run up completed and found {count} migrations",
            run.Count
          )

          run |> Seq.toArray |> lastDryRun.setValue

          currentShow.setValue(
            MigrationRunner.CurrentShow.DryRun
              MigrationRunner.RunMigrationKind.Up
          )

          logger.LogDebug("Current show set to dry run up")
          return ()
        | MigrationRunner.RunMigrationKind.DryDown ->
          logger.LogDebug("Running migrations dry down")
          let! run = ops.Core.DryRunDownAsync(steps, cancellationToken = token)

          logger.LogDebug(
            "Dry run down completed and found {count} migrations",
            run.Count
          )

          run |> Seq.toArray |> lastDryRun.setValue

          currentShow.setValue(
            MigrationRunner.CurrentShow.DryRun
              MigrationRunner.RunMigrationKind.Down
          )

          logger.LogDebug("Current show set to dry run down")
          return ()
      }
      |> handleError

    member _.UpdateProject(project: VirtualProject) = asyncEx {
      logger.LogDebug("Updating project: {project}", project.name)

      try
        do! projects.UpdateVirtual project
        _project.setValue project
        return ()
      with ex ->
        logger.LogError("Unable to update the project due: {error}", ex)
        return ()
    }

    member _.NavigateBack() : Async<unit> = asyncEx {
      match! navigator.NavigateByName Routes.LandingRouteName with
      | Ok _ -> return ()
      | Error(e) -> logger.LogWarning("Navigation failed: {error}", e)
    }

    member this.RemoveMigration(migration: Migration) = asyncEx {
      let! deleted = this.DeleteMigration migration

      if deleted then
        do! this.ListMigrations()

      return ()
    }
