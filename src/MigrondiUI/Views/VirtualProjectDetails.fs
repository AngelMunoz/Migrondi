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
open MigrondiUI.Logic
open MigrondiUI.Logic.VirtualProjectDetails
open MigrondiUI.Components
open MigrondiUI.Components.MigrationRunnerToolbar

let virtualProjectView
  (
    project: VirtualProject aval,
    onRunMigrationsRequested: MigrationRunner.RunMigrationKind * int -> unit,
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
      .PlaceholderText("Enter migration name")
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
        let text =
          match nameTextBox.Text with
          | null -> ""
          | value -> value.Trim().Replace(' ', '-')

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
    navigator: INavigator
  ) = asyncEx {
  let! project = projects.Get projectId

  match project with
  | Some(Virtual p) ->
    let vm =
      VirtualProjectDetailsVM(logger, projects, migrondiFactory, p, navigator)

    let onNavigateBack() =
      vm.NavigateBack() |> Async.StartImmediate

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
  let navigator = AvaloniaNavigator nav

  let view =
    let projectId = defaultArg projectId Guid.Empty
    cval(buildLoading projectId)

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
          navigator
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
