module MigrondiUI.Views.LocalProjectDetails

open System
open System.IO
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open Avalonia.Controls

open NXUI.Extensions

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders
open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Navs
open Navs.Avalonia
open Migrondi.Core

open MigrondiUI
open MigrondiUI.Services
open MigrondiUI.Logic
open MigrondiUI.Logic.LocalProjectDetails
open MigrondiUI.Components
open MigrondiUI.Components.MigrationRunnerToolbar
open MigrondiUI.Components.Fields

let localProjectView
  (
    project: LocalProject,
    onRunMigrationsRequested: MigrationRunner.RunMigrationKind * int -> unit
  ) : Control =
  let description = defaultArg project.description "No description"

  let config =
    project.config |> Option.defaultWith(fun _ -> failwith "No config found")

  let configView(configPath: string, config: MigrondiConfig) : Control =
    let migrationsDir =
      option {
        let! path = Path.GetDirectoryName(configPath)
        let path = Path.Combine(path, config.migrations)
        return Path.GetFullPath path
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
          MigrationsRunnerToolbar(onRunMigrationsRequested)
            .VerticalAlignmentCenter()
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


type LProjectDetailsView(vm: LocalProjectDetailsVM, onNavigateBack) =
  inherit UserControl()

  let onNewMigration(name: string) =
    vm.NewMigration name |> Async.StartImmediate

  let onRefresh() =
    vm.ListMigrations() |> Async.StartImmediate

  let onOpenInExplorer() =
    vm.OpenFileExplorer() |> Async.StartImmediate

  let onRunMigrationsRequested args =
    vm.RunMigrations args |> Async.StartImmediate

  do
    vm.ListMigrations() |> Async.StartImmediate

    base.Name <- "ProjectDetails"

    base.Content <-
      Grid()
        .RowDefinitions("Auto,Auto,*")
        .ColumnDefinitions("Auto,*,*")
        .Children(
          toolbar(onNavigateBack, onNewMigration, onRefresh, onOpenInExplorer)
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

let buildDetailsView
  (
    projectId: Guid,
    logger: ILogger<LocalProjectDetailsVM>,
    projects: Services.IProjectCollection,
    migrondiFactory: Services.IMigrationOperationsFactory,
    navigator: INavigator
  ) = asyncEx {
  let! project = projects.Get projectId

  match project with
  | Some(Local p) ->
    let vm =
      LocalProjectDetailsVM(logger, projects, migrondiFactory, p, navigator)

    let onNavigateBack() =
      vm.NavigateBack() |> Async.StartImmediate

    return Some(LProjectDetailsView(vm, onNavigateBack) :> Control)
  | Some(Virtual _) -> return failwith "Expected local project but got virtual"
  | None -> return None
}

let buildProjectNotFound(id: Guid) : Control =
  UserControl()
    .Name("ProjectNotFound")
    .Content(TextBlock().Text($"Project with ID {id} was not found."))

let buildLoading(id: Guid) : Control =
  UserControl()
    .Name("ProjectLoading")
    .Content(TextBlock().Text($"Loading project with ID {id}..."))

let View
  (
    logger: ILogger<LocalProjectDetailsVM>,
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
      | Some builtView -> view.setValue(builtView)
      | None -> view.setValue(buildProjectNotFound projectId)
    }
    |> Async.StartImmediate
  | None ->
    logger.LogDebug("No project ID found in route parameters")
    view.setValue(buildProjectNotFound Guid.Empty)

  UserControl()
    .Name("LocalProjectDetails")
    .Content(view |> AVal.toBinding)
    .Margin(8)
