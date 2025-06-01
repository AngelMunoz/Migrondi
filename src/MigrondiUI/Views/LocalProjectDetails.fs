module MigrondiUI.Views.LocalProjectDetails

open System
open System.IO
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open Avalonia.Controls

open NXUI.Extensions

open IcedTasks
open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Navs
open Navs.Avalonia
open Migrondi.Core

open MigrondiUI
open MigrondiUI.Projects
open MigrondiUI.Components
open MigrondiUI.Components.MigrationRunnerToolbar
open MigrondiUI.Components.Fields

type LocalProjectDetailsVM
  (
    logger: ILogger<LocalProjectDetailsVM>,
    migrondi: IMigrondi,
    project: LocalProject
  ) =

  let _migrations = cval [||]
  let lastDryRun = cval [||]

  let currentShow = cval ProjectDetails.CurrentShow.Migrations

  let handleException(work: Async<unit>) = asyncEx {
    let! result = work |> Async.Catch

    match result with
    | Choice1Of2 _ -> return ()
    | Choice2Of2 ex ->
      logger.LogError(ex, "An error occurred while processing the request")
      currentShow.setValue(ProjectDetails.CurrentShow.ExceptionThrown ex)
      return ()
  }

  do
    logger.LogDebug "LocalProjectDetailsVM created"
    migrondi.Initialize()

  member _.Project = project

  member _.Migrations: MigrationStatus[] aval = _migrations

  member _.LastDryRun: Migration[] aval = lastDryRun

  member _.CurrentShow: ProjectDetails.CurrentShow aval = currentShow

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
    let! token = Async.CancellationToken
    let! migrations = migrondi.MigrationsListAsync token

    logger.LogDebug("Migrations listed: {migrations}", migrations.Count)

    migrations |> Seq.toArray |> _migrations.setValue

    lastDryRun.setValue [||]
    logger.LogDebug "Last dry run set to empty array"

    currentShow.setValue ProjectDetails.CurrentShow.Migrations
    return ()
  }

  member this.NewMigration(name: string) =
    asyncEx {
      logger.LogDebug "Creating new migration"
      let! token = Async.CancellationToken

      logger.LogDebug("New migration name: {name}", name)
      let! migration = migrondi.RunNewAsync(name, cancellationToken = token)
      logger.LogDebug("New migration created: {migration}", migration)
      return! this.ListMigrations()
    }
    |> handleException

  member this.RunMigrations(kind: ProjectDetails.RunMigrationKind, steps: int) =
    asyncEx {
      let! token = Async.CancellationToken
      logger.LogDebug("Running migrations: {kind}, {steps}", kind, steps)

      match kind with
      | ProjectDetails.RunMigrationKind.Up ->
        logger.LogDebug("Running migrations up")
        do! migrondi.RunUpAsync(steps, cancellationToken = token) :> Task
        do! this.ListMigrations()
        logger.LogDebug("Migrations up completed")
        return ()
      | ProjectDetails.RunMigrationKind.Down ->
        logger.LogDebug("Running migrations down")
        do! migrondi.RunDownAsync(steps, cancellationToken = token) :> Task
        do! this.ListMigrations()
        logger.LogDebug("Migrations down completed")
        return ()
      | ProjectDetails.RunMigrationKind.DryUp ->
        logger.LogDebug("Running migrations dry up")
        let! run = migrondi.DryRunUpAsync(steps, cancellationToken = token)

        logger.LogDebug(
          "Dry run up completed and found {count} migrations",
          run.Count
        )

        run |> Seq.toArray |> lastDryRun.setValue

        currentShow.setValue(
          ProjectDetails.CurrentShow.DryRun ProjectDetails.RunMigrationKind.Up
        )

        logger.LogDebug("Current show set to dry run up")
        return ()
      | ProjectDetails.RunMigrationKind.DryDown ->
        logger.LogDebug("Running migrations dry down")
        let! run = migrondi.DryRunDownAsync(steps, cancellationToken = token)

        logger.LogDebug(
          "Dry run down completed and found {count} migrations",
          run.Count
        )

        run |> Seq.toArray |> lastDryRun.setValue

        currentShow.setValue(
          ProjectDetails.CurrentShow.DryRun ProjectDetails.RunMigrationKind.Down
        )

        logger.LogDebug("Current show set to dry run down")
        return ()
    }
    |> handleException

  // Reusing shared component from SharedComponents module

let localProjectView
  (
    project: LocalProject,
    onRunMigrationsRequested: ProjectDetails.RunMigrationKind * int -> unit
  ) : Control =
  let description = defaultArg project.description "No description"

  let config =
    project.config |> Option.defaultWith(fun _ -> failwith "No config found")

  let configView(configPath: string, config: MigrondiConfig) : Control =
    let migrationsDir =
      option {
        let! path = configPath |> Path.GetDirectoryName
        return Path.Combine(path, config.migrations) |> Path.GetFullPath
      }
      |> Option.defaultValue "Unable to resolve the project's root directory"

    Grid()
      .RowDefinitions("*,Auto,Auto")
      .ColumnDefinitions("10*,90*")
      .Children(
        LabeledField
          .Horizontal("Connection String:", config.connection)
          .Row(0)
          .Column(0)
          .ColumnSpan(2),
        LabeledField.Horizontal("Driver:", $"{config.driver}").Row(1).Column(0),
        LabeledField
          .Horizontal("Migrations Directory:", migrationsDir)
          .Row(1)
          .Column(1)
      )

  Expander()
    .Header(
      StackPanel()
        .Spacing(8)
        .OrientationHorizontal()
        .Children(
          TextBlock()
            .Text($"{project.name} - {description}")
            .VerticalAlignmentCenter(),
          MigrationsRunnerToolbar(onRunMigrationsRequested).VerticalAlignmentCenter()
        )
    )
    .Content(configView(project.migrondiConfigPath, config))

let toolbar
  (
    onNavigateBack: unit -> unit,
    onNewMigration: string -> unit,
    onRefresh: unit -> unit,
    onOpenInExplorer: unit -> unit
  ) : Control =
  let isEnabled = cval false

  let nameTextBox =
    TextBox()
      .Name("New Migration Name:")
      .Watermark("Enter migration name")
      .Width(200)
      .AcceptsReturn(false)
      .OnTextChangedHandler(fun txtBox _ ->
        if String.IsNullOrWhiteSpace txtBox.Text |> not then
          isEnabled.setValue true
        else
          isEnabled.setValue false)

  let createButton =
    Button()
      .Content("Create Migration")
      .IsEnabled(isEnabled |> AVal.toBinding)
      .OnClickHandler(fun _ _ ->
        let text = (nameTextBox.Text |> nonNull).Trim().Replace(' ', '-')
        onNewMigration text
        nameTextBox.Text <- "")

  let openInExplorerButton =
    Button()
      .Content("Open in Explorer")
      .OnClickHandler(fun _ _ -> onOpenInExplorer())

  Toolbar
    .get(Spacing 8., Orientation Horizontal)
    .Children(
      Button().Content("Back").OnClickHandler(fun _ _ -> onNavigateBack()),
      Button().Content("Refresh").OnClickHandler(fun _ _ -> onRefresh()),
      nameTextBox,
      createButton,
      openInExplorerButton
    )

let View
  (
    logger: ILogger<LocalProjectDetailsVM>,
    mLogger: ILogger<IMigrondi>,
    projects: ILocalProjectRepository
  )
  (context: RouteContext)
  (nav: INavigable<Control>)
  : Async<Control> =
  asyncEx {
    let getProjectbyId(projectId: Guid) = asyncEx {
      let! project = projects.GetProjectById projectId

      match project with
      | Some project -> return Some project
      | _ ->
        logger.LogWarning(
          "We're not supposed to have a virtual project here. Project ID: {projectId}",
          projectId
        )

        return None
    }

    let vm = asyncOption {
      let! projectId =
        context.getParam<Guid> "projectId" |> ValueOption.toOption

      logger.LogDebug(
        "Project ID from route parameters: {projectId}",
        projectId
      )

      let! project = getProjectbyId projectId
      logger.LogDebug("Project from repository: {project}", project)

      let! config = project.config

      let projectRoot =
        Path.GetDirectoryName project.migrondiConfigPath |> nonNull

      let migrondi = Migrondi.MigrondiFactory(config, projectRoot, mLogger)

      return LocalProjectDetailsVM(logger, migrondi, project)
    }

    let onNavigateBack() =
      asyncEx {
        match! nav.NavigateByName("landing") with
        | Ok _ -> ()
        | Error(e) ->
          logger.LogWarning("Navigation failed: {error}", e.StringError())
      }
      |> Async.StartImmediate

    match! vm with
    | Some vm ->
      vm.ListMigrations() |> Async.StartImmediate

      let onNewMigration name =
        vm.NewMigration name |> Async.StartImmediate

      let onRefresh() =
        vm.ListMigrations() |> Async.StartImmediate

      let onOpenInExplorer() =
        vm.OpenFileExplorer() |> Async.StartImmediate

      let onRunMigrationsRequested args =
        vm.RunMigrations args |> Async.StartImmediate

      return
        UserControl()
          .Name("ProjectDetails")
          .Content(
            Grid()
              .RowDefinitions("Auto,Auto,*")
              .ColumnDefinitions("Auto,*,*")
              .Children(
                toolbar(
                  onNavigateBack,
                  onNewMigration,
                  onRefresh,
                  onOpenInExplorer
                )
                  .Row(0)
                  .Column(0)
                  .ColumnSpan(2)
                  .HorizontalAlignmentStretch(),
                localProjectView(vm.Project, onRunMigrationsRequested)
                  .Row(1)
                  .Column(0)
                  .ColumnSpan(3)
                  .VerticalAlignmentTop()
                  .HorizontalAlignmentStretch()
                  .MarginY(8),
                ProjectDetails
                  .MigrationsPanel(
                    currentShow = vm.CurrentShow,
                    migrations = vm.Migrations,
                    lastDryRun = vm.LastDryRun,
                    migrationsView = ProjectDetails.migrationListView,
                    dryRunView = ProjectDetails.dryRunListView
                  )
                  .Row(2)
                  .Column(0)
                  .ColumnSpan(3)
                  .VerticalAlignmentStretch()
                  .HorizontalAlignmentStretch()
                  .MarginY(8)
              )
          )
          .Margin(8)
        :> Control
    | None ->
      logger.LogWarning("Project ID not found in route parameters.")
      return TextBlock().Text("Project ID not found.")
  }
