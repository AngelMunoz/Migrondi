module MigrondiUI.Views.LocalProjectDetails

open System
open System.IO

open Microsoft.Extensions.Logging

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Media

open NXUI.Extensions

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders
open FsToolkit.ErrorHandling
open FSharp.Data.Adaptive

open Navs
open Navs.Avalonia
open Migrondi.Core
open MigrondiUI
open MigrondiUI.Projects
open System.Threading.Tasks

[<Struct>]
type RunMigrationKind =
  | Up
  | Down
  | DryUp
  | DryDown

[<Struct>]
type CurrentShow =
  | Migrations
  | DryRun

type LocalProjectDetailsVM
  (
    logger: ILogger<LocalProjectDetailsVM>,
    migrondi: IMigrondi,
    project: LocalProject
  ) =

  let _migrations = cval [||]
  let lastDryRun = cval [||]

  let currentShow = cval Migrations

  do
    logger.LogDebug "LocalProjectDetailsVM created"
    migrondi.Initialize()

  member _.Project = project

  member _.Migrations: MigrationStatus[] aval = _migrations

  member _.LastDryRun: Migration[] aval = lastDryRun

  member _.CurrentShow: CurrentShow aval = currentShow

  member _.ListMigrations() = async {
    logger.LogDebug "Listing migrations"
    let! token = Async.CancellationToken
    let! migrations = migrondi.MigrationsListAsync(token)

    logger.LogDebug("Migrations listed: {migrations}", migrations.Count)

    migrations |> Seq.rev |> Seq.toArray |> _migrations.setValue

    lastDryRun.setValue [||]
    logger.LogDebug "Last dry run set to empty array"

    currentShow.setValue Migrations
    return ()
  }

  member this.NewMigration(name: string) = async {
    logger.LogDebug "Creating new migration"
    let! token = Async.CancellationToken

    logger.LogDebug("New migration name: {name}", name)
    let! migration = migrondi.RunNewAsync(name, cancellationToken = token)
    logger.LogDebug("New migration created: {migration}", migration)
    return! this.ListMigrations()
  }

  member this.RunMigrations(kind: RunMigrationKind, steps: int) = async {
    logger.LogDebug "Running migrations"
    let! token = Async.CancellationToken
    logger.LogDebug("Running migrations: {kind}, {steps}", kind, steps)

    match kind with
    | Up ->
      do! migrondi.RunUpAsync(steps, cancellationToken = token) :> Task
      do! this.ListMigrations()

    | Down ->
      do! migrondi.RunDownAsync(steps, cancellationToken = token) :> Task
      do! this.ListMigrations()

    | DryUp ->
      let! run = migrondi.DryRunUpAsync(steps, cancellationToken = token)
      run |> Seq.toArray |> lastDryRun.setValue
      currentShow.setValue DryRun
    | DryDown ->
      let! run = migrondi.DryRunDownAsync(steps, cancellationToken = token)
      run |> Seq.toArray |> lastDryRun.setValue
      currentShow.setValue DryRun

    logger.LogDebug("Migrations run completed: {result}", result)

    logger.LogDebug(
      "Current show set to: {currentShow}",
      currentShow.getValue()
    )

    return ()
  }

let migrationView =
  FuncDataTemplate<MigrationStatus>(fun migrationStatus _ ->
    let migration =
      match migrationStatus with
      | Applied m -> m
      | Pending m -> m

    let status =
      match migrationStatus with
      | Applied _ -> "Applied"
      | Pending _ -> "Pending"

    let strDate =
      DateTimeOffset.FromUnixTimeMilliseconds migration.timestamp
      |> _.ToString("G")

    let migrationContent =
      Grid()
        .ColumnDefinitions("*,4,*")
        .Children(
          LabeledField.Vertical("Migrate Up", migration.upContent).Column(0),
          GridSplitter()
            .Column(1)
            .ResizeDirectionColumns()
            .Background("Black" |> SolidColorBrush.Parse)
            .MarginX(8)
            .CornerRadius(5),
          LabeledField
            .Vertical("Migrate Down", migration.downContent)
            .Column(2)
        )

    Expander()
      .Header(
        StackPanel()
          .Children(
            TextBlock().Text(migration.name),
            TextBlock()
              .Text($" [{status}]")
              .Foreground(
                match migrationStatus with
                | Applied _ -> "Green"
                | Pending _ -> "OrangeRed"
                |> SolidColorBrush.Parse
              ),
            TextBlock().Text($" - {strDate}")
          )
          .OrientationHorizontal()
      )
      .Content(
        StackPanel()
          .Children(
            LabeledField.Horizontal(
              "Manual Transaction:",
              $" %b{migration.manualTransaction}"
            ),
            ScrollViewer().Content migrationContent
          )
          .Spacing(8)
      )
      .HorizontalAlignmentStretch()
      .VerticalAlignmentStretch()
      .MarginY(4))

let newMigrationForm(onNewMigration: string -> unit) : Control =
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
          ())

  let createButton =
    Button()
      .Content("Create Migration")
      .IsEnabled(isEnabled |> AVal.toBinding)
      .OnClickHandler(fun _ _ ->
        let text = (nameTextBox.Text |> nonNull).Trim().Replace(' ', '-')
        onNewMigration text
        nameTextBox.Text <- "")

  StackPanel()
    .OrientationHorizontal()
    .Children(nameTextBox, createButton)
    .Spacing(8)


let runMigrations
  (onRunMigrationsRequested: RunMigrationKind * int -> unit)
  : Control =
  let dryRun = cval false
  let steps = cval 1M

  let getIntValue() =
    try
      let v = steps.getValue() |> int
      if v < 0 then 1 else v
    with :? OverflowException ->
      1



  let applyPendingButton =
    let abutton =
      dryRun
      |> AVal.map(fun dryRun ->
        if dryRun then
          Button()
            .Content("Apply Pending (Dry Run)")
            .OnClickHandler(fun _ _ ->
              onRunMigrationsRequested(DryUp, getIntValue()))
          :> Control
        else
          SplitButton()
            .Content("Apply Pending")
            .Flyout(
              Flyout()
                .Content(
                  Button()
                    .Content("Confirm Apply")
                    .OnClickHandler(fun _ _ ->
                      onRunMigrationsRequested(Up, getIntValue()))
                )
            ))

    UserControl().Name("ApplyPendingButton").Content(abutton |> AVal.toBinding)

  let rollbackButton =
    let rbutton =
      dryRun
      |> AVal.map(fun dryRun ->
        if dryRun then
          Button()
            .Content("Rollback (Dry Run)")
            .OnClickHandler(fun _ _ ->
              onRunMigrationsRequested(DryDown, getIntValue()))
          :> Control
        else
          SplitButton()
            .Content("Rollback")
            .Flyout(
              Flyout()
                .Content(
                  Button()
                    .Content("Confirm Rollback")
                    .OnClickHandler(fun _ _ ->
                      onRunMigrationsRequested(Down, getIntValue()))
                )
            ))

    UserControl().Name("RollbackButton").Content(rbutton |> AVal.toBinding)

  // Define the NumericUpDown
  let numericUpDown =
    NumericUpDown()
      .Minimum(0)
      .Value(steps |> AVal.toBinding)
      .Watermark("Amount to run")
      .OnValueChangedHandler(fun _ value ->
        match value.NewValue |> ValueOption.ofNullable with
        | ValueNone -> steps.setValue 1M
        | ValueSome value -> steps.setValue value)

  // Define the CheckBox
  let checkBox =
    CheckBox()
      .Content("Dry Run")
      .IsChecked(dryRun |> AVal.toBinding)
      .OnIsCheckedChangedHandler(fun checkbox _ ->

        let isChecked =
          checkbox.IsChecked
          |> ValueOption.ofNullable
          |> ValueOption.defaultValue true

        dryRun.setValue isChecked)

  StackPanel()
    .OrientationHorizontal()
    .Spacing(8)
    .Children(applyPendingButton, rollbackButton, checkBox, numericUpDown)

let toolbar
  (
    onNavigateBack: unit -> unit,
    onNewMigration: string -> unit,
    onRefresh: unit -> unit,
    onRunMigrationsRequested: RunMigrationKind * int -> unit
  ) : Control =
  StackPanel()
    .OrientationHorizontal()
    .Spacing(8)
    .Children(
      Button().Content("Back").OnClickHandler(fun _ _ -> onNavigateBack()),
      Button().Content("Refresh").OnClickHandler(fun _ _ -> onRefresh()),
      newMigrationForm(onNewMigration),
      runMigrations(onRunMigrationsRequested)
    )

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

let localProjectView(project: LocalProject) : Control =
  let description = defaultArg project.description "No description"

  let config =
    project.config |> Option.defaultWith(fun _ -> failwith "No config found")

  Expander()
    .Header($"{project.name} - {description}")
    .Content(configView(project.migrondiConfigPath, config))

let migrationsPanel
  (
    currentShow: aval<CurrentShow>,
    migrations: aval<MigrationStatus[]>,
    lastDryRun: aval<Migration[]>
  ) : Control =

  let migrationListView =
    migrations
    |> AVal.map(fun migrations ->
      if migrations.Length = 0 then
        TextBlock().Text "No migrations found." :> Control
      else
        ItemsControl().ItemsSource(migrations).ItemTemplate(migrationView))

  //TODO: change view based on currentShow
  // migrations should show when currentShow is Migrations
  // lastDryRun should show when currentShow is DryRun
  // lastDryRun will use a SelectableTextBox for the content
  ScrollViewer().Content(migrationListView |> AVal.toBinding)

let View
  (
    logger: ILogger<LocalProjectDetailsVM>,
    mLogger: ILogger<IMigrondi>,
    projects: IProjectRepository
  )
  (context: RouteContext)
  (nav: INavigable<Control>)
  : Async<Control> =
  async {
    let getProjectbyId(projectId: Guid) = async {
      let! project = projects.GetProjectById projectId

      match project with
      | Some(Local project) -> return Some project
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
      async {
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

      let onRunMigrationsRequested(kind, steps) =
        vm.RunMigrations(kind, steps) |> Async.StartImmediate

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
                  onRunMigrationsRequested
                )
                  .Row(0)
                  .Column(0)
                  .ColumnSpan(2)
                  .HorizontalAlignmentStretch(),
                localProjectView(vm.Project)
                  .Row(1)
                  .Column(0)
                  .ColumnSpan(3)
                  .VerticalAlignmentTop()
                  .HorizontalAlignmentStretch()
                  .MarginY(8),
                migrationsPanel(vm.CurrentShow, vm.Migrations, vm.LastDryRun)
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
