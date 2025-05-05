namespace MigrondiUI.Views

open System
open System.IO
open Microsoft.Extensions.Logging
open Avalonia.Controls
open NXUI.Extensions
open FSharp.Data.Adaptive
open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders
open FsToolkit.ErrorHandling
open Navs
open Navs.Avalonia
open Migrondi.Core
open MigrondiUI
open MigrondiUI.Projects

module LocalProjectDetails =
  open Avalonia.Controls.Templates
  open Avalonia.Media


  type LocalProjectDetailsVM
    (
      logger: ILogger<LocalProjectDetailsVM>,
      migrondi: IMigrondi,
      project: LocalProject
    ) =

    let _migrations = cval [||]

    do
      logger.LogDebug "LocalProjectDetailsVM created"
      migrondi.Initialize()

    member _.Project = project

    member _.Migrations: MigrationStatus[] aval = _migrations

    member _.ListMigrations() = async {
      logger.LogDebug "Listing migrations"
      let! token = Async.CancellationToken
      let! migrations = migrondi.MigrationsListAsync(token)

      logger.LogDebug("Migrations listed: {migrations}", migrations.Count)

      _migrations.setValue [| yield! migrations |]

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
        |> _.ToString("R")

      let migrationContent =
        Grid()
          .ColumnDefinitions("*,4,*")
          .Children(
            LabeledField.Vertical("Migrate Up", migration.upContent).Column(0),
            GridSplitter().Column(1).ResizeDirectionColumns(),
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
        .VerticalAlignmentStretch())

  let toolbar(onNavigateBack: unit -> unit) : Control =
    StackPanel()
      .OrientationHorizontal()
      .Spacing(10)
      .Children(
        Button().Content("Back").OnClickHandler(fun _ _ -> onNavigateBack())
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

  let migrationsPanel(migrations: aval<MigrationStatus[]>) : Control =

    let migrationListView =
      migrations
      |> AVal.map(fun migrations ->
        if migrations.Length = 0 then
          TextBlock().Text "No migrations found." :> Control
        else
          ItemsControl().ItemsSource(migrations).ItemTemplate(migrationView))

    ScrollViewer().Content(migrationListView |> AVal.toBinding)

  let View
    (logger: ILogger<LocalProjectDetailsVM>, projects: IProjectRepository)
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

        let migrondi = Migrondi.MigrondiFactory(config, projectRoot)

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

        return
          UserControl()
            .Name("ProjectDetails")
            .Content(
              Grid()
                .RowDefinitions("Auto,Auto,*")
                .ColumnDefinitions("Auto,*,*")
                .Children(
                  toolbar(onNavigateBack)
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
                  migrationsPanel(vm.Migrations)
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
