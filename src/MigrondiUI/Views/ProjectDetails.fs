namespace MigrondiUI.Views

open System
open Avalonia.Controls
open NXUI.Extensions
open FSharp.Data.Adaptive
open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders
open FsToolkit.ErrorHandling
open Navs
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Projects

module LocalProjectDetails =
  open Microsoft.Extensions.Logging
  open Migrondi.Core
  open System.IO

  type LocalProjectDetailsVM
    (
      logger: ILogger<LocalProjectDetailsVM>,
      migrondi: IMigrondi,
      project: LocalProject
    ) =

    do logger.LogDebug "LocalProjectDetailsVM created"


    member _.Project = project


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

        let migrondi =
          Migrondi.MigrondiFactory(config, project.migrondiConfigPath)

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
        return
          UserControl()
            .Name("ProjectDetails")
            .Content(
              Grid()
                .RowDefinitions("5*,95*")
                .ColumnDefinitions("Auto,*")
                .Children(
                  toolbar(onNavigateBack)
                    .Row(0)
                    .Column(0)
                    .ColumnSpan(2)
                    .HorizontalAlignmentStretch(),
                  localProjectView(vm.Project)
                    .Row(1)
                    .Column(0)
                    .ColumnSpan(2)
                    .VerticalAlignmentTop()
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
