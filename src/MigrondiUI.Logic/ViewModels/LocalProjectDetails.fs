namespace MigrondiUI.Logic

module LocalProjectDetails =

  open System
  open System.IO
  open System.Threading.Tasks

  open Microsoft.Extensions.Logging

  open IcedTasks
  open IcedTasks.Polyfill.Async.PolyfillBuilders
  open FSharp.Data.Adaptive

  open Migrondi.Core
  open MigrondiUI
  open MigrondiUI.Services

  type LocalProjectDetailsVM
    (
      logger: ILogger<LocalProjectDetailsVM>,
      projects: IProjectCollection,
      migrondiFactory: IMigrationOperationsFactory,
      project: LocalProject,
      navigator: INavigator
    ) =

    let _migrations = cval [||]
    let lastDryRun = cval [||]

    let currentShow = cval MigrationRunner.CurrentShow.Migrations

    let handleException(work: Async<unit>) = asyncEx {
      let! result = work |> Async.Catch

      match result with
      | Choice1Of2 _ -> return ()
      | Choice2Of2 ex ->
        logger.LogError(ex, "An error occurred while processing the request")
        currentShow.setValue(MigrationRunner.CurrentShow.ExceptionThrown ex)
        return ()
    }

    let ops = migrondiFactory.Create(Local project)

    do
      logger.LogDebug "LocalProjectDetailsVM created"
      ops.Core.Initialize()

    member _.Project = project

    member _.Migrations: MigrationStatus[] aval = _migrations

    member _.LastDryRun: Migration[] aval = lastDryRun

    member _.CurrentShow: MigrationRunner.CurrentShow aval = currentShow

    member _.OpenFileExplorer() = asyncEx {
      logger.LogDebug "Opening file explorer"

      let migrationsDir =
        project.migrondiConfigPath |> Path.GetDirectoryName |> nonNull

      logger.LogDebug("Migrations directory: {migrationsDir}", migrationsDir)

      do! OsOperations.OpenDirectory migrationsDir
      logger.LogDebug "File explorer opened"
    }

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

    member this.NewMigration(name: string) =
      asyncEx {
        logger.LogDebug "Creating new migration"

        // token edge: defaults to None internally
        let! ct = Async.CancellationToken

        logger.LogDebug("New migration name: {name}", name)
        let! _ = ops.Core.RunNewAsync(name, cancellationToken = ct)
        logger.LogDebug("New migration created")
        return! this.ListMigrations()
      }
      |> handleException

    member this.RunMigrations
      (kind: MigrationRunner.RunMigrationKind, steps: int)
      =
      asyncEx {
        // token edge: defaults to None internally
        let! ct = Async.CancellationToken
        logger.LogDebug("Running migrations: {kind}, {steps}", kind, steps)

        match kind with
        | MigrationRunner.RunMigrationKind.Up ->
          logger.LogDebug("Running migrations up")
          do! ops.Core.RunUpAsync(steps, cancellationToken = ct) :> Task
          do! this.ListMigrations()
          logger.LogDebug("Migrations up completed")
          return ()
        | MigrationRunner.RunMigrationKind.Down ->
          logger.LogDebug("Running migrations down")
          do! ops.Core.RunDownAsync(steps, cancellationToken = ct) :> Task
          do! this.ListMigrations()
          logger.LogDebug("Migrations down completed")
          return ()
        | MigrationRunner.RunMigrationKind.DryUp ->
          logger.LogDebug("Running migrations dry up")
          let! run = ops.Core.DryRunUpAsync(steps, cancellationToken = ct)

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
          let! run = ops.Core.DryRunDownAsync(steps, cancellationToken = ct)

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
      |> handleException

    member _.NavigateBack() : Async<unit> = asyncEx {
      match! navigator.NavigateByName Routes.LandingRouteName with
      | Ok _ -> return ()
      | Error(e) -> logger.LogWarning("Navigation failed: {error}", e)
    }
