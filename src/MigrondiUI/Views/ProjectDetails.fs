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

module ProjectDetails =
  open Microsoft.Extensions.Logging

  type ProjectDetailsVM
    (
      logger: ILogger<ProjectDetailsVM>,
      projects: IProjectRepository,
      projectId: Guid
    ) =
    let _project = cval None

    do logger.LogDebug "ProjectDetailsVM created"

    member _.ProjectId: Guid = projectId

    member _.Project: Project option aval = _project

    member _.LoadProject() = async {
      let! projectOpt = projects.GetProjectById projectId
      _project.setValue projectOpt
      return ()
    }

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

  let virtualProjectView(project: VirtualProject) : Control =
    TextBlock()
      .Text(
        $"Virtual project: {project.name} with connection {project.connection}"
      )

  let viewContent(vm: ProjectDetailsVM) : Control =
    let content =
      vm.Project
      |> AVal.map (function
        | Some(Local project) -> localProjectView project
        | Some(Virtual project) -> virtualProjectView project
        | None -> TextBlock().Text("Project not found"))
      |> AVal.toBinding

    Border().Child(content)

  let View
    (logger, projects)
    (context: RouteContext)
    (nav: INavigable<Control>)
    : Async<Control> =
    async {
      let vm = voption {
        let! projectId = context.getParam<Guid>("projectId")
        return ProjectDetailsVM(logger, projects, projectId)
      }

      let onNavigateBack() =
        async {
          match! nav.NavigateByName("landing") with
          | Ok _ -> ()
          | Error(NavigationFailed e) ->
            logger.LogWarning("Navigation failed: {error}", e)
          | err -> logger.LogError("Unknown navigation error: {error}", err)
        }
        |> Async.StartImmediate


      match vm with
      | ValueSome vm ->
        vm.LoadProject() |> Async.StartImmediate
        let view = UserControl()

        return
          view
            .Name("ProjectDetails")
            .Tag(vm.ProjectId)
            .Content(
              DockPanel()
                .LastChildFill(true)
                .Children(
                  toolbar(onNavigateBack).DockTop(),
                  viewContent(vm).DockTop()
                )
                .Margin(10)
            )
          :> Control
      | ValueNone ->
        logger.LogWarning("Project ID not found in route parameters.")
        return TextBlock().Text("Project ID not found.")
    }
