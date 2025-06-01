module MigrondiUI.Views.VirtualProjectDetails

open System
open System.IO
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open Avalonia.Controls
open Avalonia.Controls.Templates
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

open MigrondiUI.MigrondiExt

type VirtualProjectDetailsVM
  (
    logger: ILogger<VirtualProjectDetailsVM>,
    migrondi: IMigrondiUI,
    vprojects: IVirtualProjectRepository,
    project: VirtualProject
  ) =
  let _migrations = cval [||]
  let lastDryRun = cval [||]
  let currentShow = cval ProjectDetails.CurrentShow.Migrations

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
    migrondi.Initialize()

  member _.Project = project

  member _.Migrations: MigrationStatus[] aval = _migrations

  member _.LastDryRun: Migration[] aval = lastDryRun

  member _.CurrentShow: ProjectDetails.CurrentShow aval = currentShow

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

  member this.CreateNewMigration(name: string) =
    asyncEx {
      logger.LogDebug("Creating new migration with name: {name}", name)

      let! token = Async.CancellationToken
      do! migrondi.RunNewAsync(name, cancellationToken = token) :> Task
      return! this.ListMigrations()
    }
    |> handleError

  member _.SaveMigration(migration: Migration) = asyncEx {
    logger.LogDebug("Saving migration: {migration}", migration.name)
    let! virtualMigration = vprojects.GetMigrationByName(migration.name)

    match virtualMigration with
    | None -> return false
    | Some virtualMigration ->
      logger.LogDebug("Migration found: {migration}", virtualMigration.id)

      try
        do!
          vprojects.UpdateMigration {
            virtualMigration with
                upContent = migration.upContent
                downContent = migration.downContent
          }

        return true
      with ex ->
        logger.LogError("Unable to save the migration due: {error}", ex)
        return false
  }

  member _.DeleteMigration(migration: Migration) = asyncEx {
    logger.LogDebug("Deleting migration: {migration}", migration.name)

    try
      do! vprojects.RemoveMigrationByName migration.name
      return true
    with ex ->
      logger.LogError("Unable to delete the migration due: {error}", ex)
      return false
  }

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
    |> handleError

  member _.UpdateProject(project: VirtualProject) = asyncEx {
    logger.LogDebug("Updating project: {project}", project.name)
    try
      do! vprojects.UpdateProject project
      return ()
    with ex ->
      logger.LogError("Unable to update the project due: {error}", ex)
      return ()
  }

let virtualProjectView
  (project: VirtualProject,
   onRunMigrationsRequested: ProjectDetails.RunMigrationKind * int -> unit,
   onSave: VirtualProject -> Async<unit>) : Control =

  Expander()
    .Header(
      StackPanel()
        .Spacing(8)
        .OrientationHorizontal()
        .Children(
          TextBlock()
            .Text("Project Details")
            .VerticalAlignmentCenter(),
          MigrationsRunnerToolbar(onRunMigrationsRequested).VerticalAlignmentCenter()
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

let View
  (
    logger: ILogger<VirtualProjectDetailsVM>,
    projects: IVirtualProjectRepository,
    vMigrondiFactory: MigrondiConfig * string * Guid -> IMigrondiUI
  )
  (context: RouteContext)
  (nav: INavigable<Control>)
  : Async<Control> =
  asyncEx {
    let getProjectById(projectId: Guid) = asyncEx {
      let! project = projects.GetProjectById projectId

      match project with
      | Some project -> return Some project
      | _ ->
        logger.LogWarning(
          "Virtual project not found. Project ID: {projectId}",
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

      let! project = getProjectById projectId
      logger.LogDebug("Project from repository: {project}", project)

      let migrondi =

        let projectRoot =
          let rootPath = Path.Combine(Path.GetTempPath(), project.id.ToString())

          try
            Directory.CreateDirectory(rootPath).FullName
          with ex ->
            logger.LogWarning(
              "Exception thrown while creating project root directory: {error}",
              ex
            )

            Path.GetFullPath rootPath

        logger.LogDebug(
          "Using project root directory: {projectRoot}",
          projectRoot
        )

        vMigrondiFactory(project.ToMigrondiConfig(), projectRoot, project.id)

      return VirtualProjectDetailsVM(logger, migrondi, projects, project)
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

      let onSaveProject project =
        vm.UpdateProject project

      return
        UserControl()
          .Name("VirtualProjectDetails")
          .Content(
            Grid()
              .RowDefinitions("Auto,Auto,Auto,*")
              .ColumnDefinitions("Auto,*,*")
              .Children(
                toolbar(onNavigateBack, onNewMigration, onRefresh)
                  .Row(0)
                  .Column(0)
                  .ColumnSpan(2)
                  .HorizontalAlignmentStretch(),
                virtualProjectView(vm.Project, onRunMigrationsRequested, onSaveProject)
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
                      ProjectDetails.templatedMigrationListView
                        migrationsViewTemplate,
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
