module MigrondiUI.Views.VirtualProjectDetails

open System
open System.IO
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open Avalonia.Controls
open Avalonia.Controls.Templates

open NXUI.Extensions

open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Navs
open Navs.Avalonia
open Migrondi.Core
open MigrondiUI
open MigrondiUI.Services
open MigrondiUI.Components
open MigrondiUI.Components.MigrationRunnerToolbar

type VirtualProjectDetailsVM
  (
    logger: ILogger<VirtualProjectDetailsVM>,
    projects: Services.IProjectCollection,
    migrondiFactory: Services.IMigrationOperationsFactory,
    project: VirtualProject
  ) =
  let _migrations = cval [||]
  let lastDryRun = cval [||]
  let currentShow = cval ProjectDetails.CurrentShow.Migrations

  let _project = cval project

  let ops = migrondiFactory.Create(Virtual project)

  let handleError(work: Async<unit>) = asyncEx {
    let! result = work |> Async.Catch

    match result with
    | Choice1Of2 _ -> return ()
    | Choice2Of2 ex ->
      logger.LogError("An error occurred: {error}", ex)
      currentShow.setValue(ProjectDetails.CurrentShow.ExceptionThrown ex)
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

  member _.CurrentShow: ProjectDetails.CurrentShow aval = currentShow

  member _.ListMigrations() = asyncEx {
    logger.LogDebug "Listing migrations"
    let! ct = Async.CancellationToken
    let! migrations = ops.Core.MigrationsListAsync ct

    logger.LogDebug("Migrations listed: {migrations}", migrations.Count)

    migrations |> Seq.toArray |> _migrations.setValue

    lastDryRun.setValue [||]
    logger.LogDebug "Last dry run set to empty array"

    currentShow.setValue ProjectDetails.CurrentShow.Migrations
    return ()
  }

  member this.CreateNewMigration(name: string) =
    asyncEx {
      logger.LogDebug("Creating new migration with name: {name}", name)

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

  member this.RunMigrations(kind: ProjectDetails.RunMigrationKind, steps: int) =
    asyncEx {
      let! token = Async.CancellationToken
      logger.LogDebug("Running migrations: {kind}, {steps}", kind, steps)

      match kind with
      | ProjectDetails.RunMigrationKind.Up ->
        logger.LogDebug("Running migrations up")
        do! ops.Core.RunUpAsync(steps, cancellationToken = token) :> Task
        do! this.ListMigrations()
        logger.LogDebug("Migrations up completed")
        return ()
      | ProjectDetails.RunMigrationKind.Down ->
        logger.LogDebug("Running migrations down")
        do! ops.Core.RunDownAsync(steps, cancellationToken = token) :> Task
        do! this.ListMigrations()
        logger.LogDebug("Migrations down completed")
        return ()
      | ProjectDetails.RunMigrationKind.DryUp ->
        logger.LogDebug("Running migrations dry up")
        let! run = ops.Core.DryRunUpAsync(steps, cancellationToken = token)

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
        let! run = ops.Core.DryRunDownAsync(steps, cancellationToken = token)

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

let virtualProjectView
  (
    project: VirtualProject aval,
    onRunMigrationsRequested: ProjectDetails.RunMigrationKind * int -> unit,
    onSave: VirtualProject -> Async<unit>
  ) : Control =
  let projectDescription =
    project
    |> AVal.map(fun p ->
      let description = defaultArg p.description "No description provided."

      let description =
        if description.Length > 24 then
          $"{description.Substring(0, 24)}..."
        else
          description

      $"{p.name} - {description}")

  Expander()
    .Header(
      StackPanel()
        .Spacing(8)
        .OrientationHorizontal()
        .Children(
          TextBlock()
            .Text(projectDescription |> AVal.toBinding)
            .VerticalAlignmentCenter(),
          MigrationsRunnerToolbar(onRunMigrationsRequested)
            .VerticalAlignmentCenter()
        )
    )
    .Content(VirtualProjectForm.VirtualProjectForm(project, onSave))
    .HorizontalAlignmentStretch()
    .VerticalAlignmentTop()

let toolbar
  (
    onNavigateBack: unit -> unit,
    onNewMigration: string -> unit,
    onRefresh: unit -> unit
  ) =
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

  Toolbar
    .get(Spacing 8., Orientation Horizontal)
    .Children(
      Button().Content("Back").OnClickHandler(fun _ _ -> onNavigateBack()),
      Button().Content("Refresh").OnClickHandler(fun _ _ -> onRefresh()),
      nameTextBox,
      createButton
    )
    .HorizontalAlignmentStretch()

type VProjectDetailsView
  (logger: ILogger, vm: VirtualProjectDetailsVM, onNavigateBack) =
  inherit UserControl()

  let onNewMigration(name: string) =
    asyncEx {
      logger.LogDebug("Creating new migration with name: {name}", name)
      do! vm.CreateNewMigration name
      logger.LogDebug("New migration created and migrations listed")
    }
    |> Async.StartImmediate

  let onRefresh() =
    vm.ListMigrations() |> Async.StartImmediate

  let onSaveRequested(migration: Migration) = asyncEx {
    logger.LogDebug("Saving migration: {migration}", migration)
    let! result = vm.SaveMigration(migration)
    logger.LogDebug("Migrations listed")
    return result
  }

  let onRemoveRequested migration () = asyncEx {
    logger.LogDebug("Removing migration")
    let! result = vm.DeleteMigration migration

    if result then
      do! vm.ListMigrations()

    return ()
  }

  let migrationsViewTemplate =
    FuncDataTemplate<MigrationStatus>(fun migrationStatus _ ->
      EditableMigration.EditableMigrationView(
        migrationStatus,
        onSaveRequested,
        onRemoveRequested migrationStatus.Migration
      ))

  let onRunMigrationsRequested args =
    vm.RunMigrations args |> Async.StartImmediate

  let onSaveProject project = vm.UpdateProject project

  do
    vm.ListMigrations() |> Async.StartImmediate

    base.Name <- "VirtualProjectDetails"

    base.Content <-
      Grid()
        .RowDefinitions("Auto,Auto,Auto,*")
        .ColumnDefinitions("Auto,*,*")
        .Children(
          toolbar(onNavigateBack, onNewMigration, onRefresh)
            .Row(0)
            .Column(0)
            .ColumnSpan(3)
            .HorizontalAlignmentStretch(),
          virtualProjectView(
            vm.Project,
            onRunMigrationsRequested,
            onSaveProject
          )
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
              migrationsView =
                ProjectDetails.templatedMigrationListView migrationsViewTemplate,
              dryRunView = ProjectDetails.dryRunListView
            )
            .Row(2)
            .Column(0)
            .ColumnSpan(3)
            .VerticalAlignmentStretch()
            .HorizontalAlignmentStretch()
            .MarginY(8)
        )

let buildDetailsView
  (
    projectId: Guid,
    logger: ILogger<VirtualProjectDetailsVM>,
    projects: Services.IProjectCollection,
    migrondiFactory: Services.IMigrationOperationsFactory,
    onNavigateBack: unit -> unit
  ) =
  asyncEx {
    let! project = projects.Get projectId

    match project with
    | Some(Virtual p) ->
      let vm = VirtualProjectDetailsVM(logger, projects, migrondiFactory, p)
      return Some(VProjectDetailsView(logger, vm, onNavigateBack) :> Control)
    | Some(Local _) -> return failwith "Expected virtual project but got local"
    | None -> return None
  }

let buildProjectNotFound(id: Guid) : Control =
  UserControl()
    .Name("VirtualProjectDetails")
    .Content(TextBlock().Text($"Project with the given id {id} was not found."))

let buildLoading(id: Guid) : Control =
  UserControl()
    .Name("VirtualProjectDetails")
    .Content(TextBlock().Text($"Loading project with the given id {id}..."))

let View
  (
    logger: ILogger<VirtualProjectDetailsVM>,
    projects: Services.IProjectCollection,
    migrondiFactory: Services.IMigrationOperationsFactory
  )
  (context: RouteContext)
  (nav: INavigable<Control>)
  : Control =

  let projectId = context.getParam<Guid> "projectId" |> ValueOption.toOption

  let view =
    let projectId = defaultArg projectId Guid.Empty
    cval(buildLoading projectId)

  let onNavigateBack() =
    asyncEx {
      match! nav.NavigateByName("landing") with
      | Ok _ -> ()
      | Error(e) ->
        logger.LogWarning("Navigation failed: {error}", e.StringError())
    }
    |> Async.StartImmediate

  match projectId with
  | Some projectId ->
    logger.LogDebug("Project ID from route parameters: {projectId}", projectId)

    asyncEx {
      match!
        buildDetailsView(
          projectId,
          logger,
          projects,
          migrondiFactory,
          onNavigateBack
        )
      with
      | Some(builtView) -> view.setValue(builtView)
      | None -> view.setValue(buildProjectNotFound projectId)
    }
    |> Async.StartImmediate
  | None ->
    logger.LogDebug("No project ID found in route parameters")
    view.setValue(buildProjectNotFound Guid.Empty)

  UserControl()
    .Name("VirtualProjectDetails")
    .Content(view |> AVal.toBinding)
    .Margin(8)
