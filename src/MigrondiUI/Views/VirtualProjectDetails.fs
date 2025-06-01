module MigrondiUI.Views.VirtualProjectDetails

open System
open System.IO

open Avalonia.Controls.Templates
open Avalonia.Media
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
open MigrondiUI.Projects
open MigrondiUI.Views.Components

open MigrondiUI.MigrondiExt
open System.Threading.Tasks

type VirtualProjectDetailsVM
  (
    logger: ILogger<VirtualProjectDetailsVM>,
    migrondi: IMigrondiUI,
    project: VirtualProject
  ) =
  let _migrations = cval [||]
  let lastDryRun = cval [||]
  let currentShow = cval ProjectDetails.CurrentShow.Migrations

  let handleError(work: Async<unit>) = async {
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

  member _.ListMigrations() = async {
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
    async {
      logger.LogDebug("Creating new migration with name: {name}", name)

      let! token = Async.CancellationToken
      do! migrondi.RunNewAsync(name, cancellationToken = token) :> Task
      return! this.ListMigrations()
    }
    |> handleError

let toolbar
  (
    (onNavigateBack: unit -> unit),
    (onNewMigration: string -> unit),
    (onRefresh: unit -> unit)
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

module EditableMigration =

  type EditableMigrationView
    (migrationStatus: MigrationStatus, onSaveRequested: Migration -> Task<bool>)
    =
    inherit UserControl()

    let migration, upContent, downContent, readonly =
      match migrationStatus with
      | Applied m -> cval m, (cval m.upContent), (cval m.downContent), true
      | Pending m -> cval m, (cval m.upContent), (cval m.downContent), false

    let isDirty =
      (migration, upContent, downContent)
      |||> AVal.map3(fun migration up down ->
        up <> migration.upContent || down <> migration.downContent)

    let status =
      match migrationStatus with
      | Applied _ -> AVal.constant "Applied"
      | Pending _ ->
        isDirty
        |> AVal.map(fun isDirty ->
          if isDirty then "Pending (Unsaved Changes)" else "Pending")

    let strDate =
      DateTimeOffset.FromUnixTimeMilliseconds(
        AVal.force migration |> _.timestamp
      )
      |> _.ToString("G")

    let migrationName = AVal.force migration |> _.name
    let manualTransaction = AVal.force migration |> _.manualTransaction

    let migrationContent =
      Grid()
        .ColumnDefinitions("*,4,*")
        .Children(
          LabeledField
            .Vertical(
              "Migrate Up",
              TextEditor.TxtEditor.ReadWrite(upContent, readonly)
            )
            .Column(0),
          GridSplitter()
            .Column(1)
            .ResizeDirectionColumns()
            .IsEnabled(false)
            .Background("Black" |> SolidColorBrush.Parse)
            .MarginX(8)
            .CornerRadius(5),
          LabeledField
            .Vertical(
              "Migrate Down",
              TextEditor.TxtEditor.ReadWrite(downContent, readonly)
            )
            .Column(2)
        )

    let saveBtn =
      UserControl()
        .Content(
          isDirty
          |> AVal.map (function
            | true ->
              let enable = cval true

              Button()
                .Content("Save")
                .IsEnabled(enable |> AVal.toBinding)
                .OnClickHandler(fun _ _ ->
                  async {
                    enable.setValue false
                    let upContent = AVal.force upContent
                    let downContent = AVal.force downContent
                    let _migration = AVal.force migration

                    let m = {
                      _migration with
                          upContent = upContent
                          downContent = downContent
                    }

                    let! saved = onSaveRequested m

                    if saved then migration.setValue m else ()
                    enable.setValue true
                  }
                  |> Async.StartImmediate)
            | false -> null)
          |> AVal.toBinding
        )

    do
      base.Content <-
        Expander()
          .Header(
            StackPanel()
              .Children(
                TextBlock().Text(migrationName),
                TextBlock()
                  .Text($" [{status}]")
                  .Foreground(
                    match migrationStatus with
                    | Applied _ -> "Green"
                    | Pending _ -> "OrangeRed"
                    |> SolidColorBrush.Parse
                  ),
                TextBlock().Text($" - {strDate}"),
                saveBtn
              )
              .OrientationHorizontal()
          )
          .Content(
            StackPanel()
              .Children(
                LabeledField.Horizontal(
                  "Manual Transaction:",
                  $" %b{manualTransaction}"
                ),
                migrationContent
              )
              .Spacing(8)
          )
          .HorizontalAlignmentStretch()
          .VerticalAlignmentStretch()
          .MarginY(4)

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

      return VirtualProjectDetailsVM(logger, migrondi, project)
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

      let onNewMigration(name: string) =
        async {
          logger.LogDebug("Creating new migration with name: {name}", name)
          do! vm.CreateNewMigration name
          logger.LogDebug("New migration created and migrations listed")
        }
        |> Async.StartImmediate

      let onRefresh() =
        vm.ListMigrations() |> Async.StartImmediate

      let onSaveRequested(migration: Migration) =
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<bool>(fun () -> task {
          logger.LogDebug("Saving migration: {migration}", migration)

          logger.LogDebug("Migrations listed")
          return true
        })

      let migrationsViewTemplate =
        FuncDataTemplate<MigrationStatus>(fun migrationStatus _ ->
          EditableMigration.EditableMigrationView(
            migrationStatus,
            onSaveRequested
          ))

      return
        UserControl()
          .Name("VirtualProjectDetails")
          .Content(
            Grid()
              .RowDefinitions("Auto,*")
              .ColumnDefinitions("Auto,*,*")
              .Children(
                toolbar(onNavigateBack, onNewMigration, onRefresh)
                  .Row(0)
                  .Column(0)
                  .ColumnSpan(2)
                  .HorizontalAlignmentStretch(),
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
                  .Row(1)
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
