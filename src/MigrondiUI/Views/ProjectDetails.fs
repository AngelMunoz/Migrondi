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

  let localProjectView(project: LocalProject) : Control =
    let description = defaultArg project.description "No description"

    let config =
      match project.config with
      | Some config -> (Decoders.migrondiConfigEncoder config).ToJsonString()
      | None ->
        "No configuration found, the project might be missing from disk."

    StackPanel()
      .Children(
        TextBlock().Text($"Project: {project.name}"),
        TextBlock().Text($"Description: {description}"),
        TextBlock().Text($"Configuration: {config}")
      )
      .Spacing(5)
      .Margin(5)
      .OrientationVertical()

  let getProjectbyId
    (logger: ILogger, projects: IProjectRepository, projectId: Guid)
    =
    async {
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

  let View
    (logger: ILogger<LocalProjectDetailsVM>, projects: IProjectRepository)
    (context: RouteContext)
    (nav: INavigable<Control>)
    : Async<Control> =
    async {
      let vm = asyncOption {
        let! projectId =
          context.getParam<Guid> "projectId" |> ValueOption.toOption

        let! project = getProjectbyId(logger, projects, projectId)

        let! config = project.config

        let migrondi = Migrondi.MigrondiFactory(config, project.rootDirectory)

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
        let view = UserControl()

        return
          view
            .Name("ProjectDetails")
            .Content(
              DockPanel()
                .LastChildFill(true)
                .Children(
                  toolbar(onNavigateBack).DockTop(),
                  localProjectView(vm.Project).DockTop()
                )
                .Margin(10)
            )
          :> Control
      | None ->
        logger.LogWarning("Project ID not found in route parameters.")
        return TextBlock().Text("Project ID not found.")
    }
