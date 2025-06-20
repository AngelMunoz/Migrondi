module MigrondiUI.Views.Landing

open System

open Microsoft.Extensions.Logging

open IcedTasks

open Avalonia.Controls
open Avalonia.Controls.Templates

open NXUI.Extensions

open FSharp.Data.Adaptive
open FsToolkit.ErrorHandling

open Navs
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Projects
open SukiUI.Controls

type LandingViewState =
  | EditProjects
  | RemoveProjects
  | Empty

type LandingVM
  (
    logger: ILogger<LandingVM>,
    projects: ILocalProjectRepository,
    vProjects: IVirtualProjectRepository
  ) =

  let _projects: Project list cval = cval []

  let viewState = cval Empty

  do logger.LogDebug "LandingVM created"

  member _.Projects: Project list aval = _projects

  member _.LoadProjects() = asyncEx {
    let! projects = projects.GetProjects()
    let! vProjects = vProjects.GetProjects()

    _projects.setValue [
      yield! projects |> List.map(fun p -> Local p)
      yield! vProjects |> List.map(fun p -> Virtual p)
    ]

    return ()
  }

  member _.SetLandingState(state: LandingViewState) =
    logger.LogDebug("Setting landing state to {State}", state)
    viewState.setValue state

  member _.EmptyViewState() =
    logger.LogDebug "Setting landing state to Empty"
    viewState.setValue Empty

  member _.ViewState: aval<LandingViewState> = viewState


let inline emptyProjectsView() : Control =
  StackPanel()
    .Children(TextBlock().Text("No projects available"))
    .Spacing(5)
    .Margin(5)
    .OrientationVertical()

let repositoryList onProjectSelected (projects: Project list aval) : Control =
  let repositoryItem =
    FuncDataTemplate<Project>(fun project _ ->
      let icon =
        match project with
        | Local _ -> "💾 (Local)"
        | Virtual _ -> "💻 (Virtual)"

      StackPanel()
        .Tag(project.Id)
        .Children(
          TextBlock().Text $"{project.Name} - {icon}",
          TextBlock().Text(defaultArg project.Description "No description")
        )
        .Spacing(5)
        .Margin(5)
        .OrientationVertical())

  let projectsListBox(projects: Project list) =

    ListBox()
      .ItemsSource(projects)
      .ItemTemplate(repositoryItem)
      .SingleSelection()
      .OnSelectionChanged<Project>(fun (args, source) ->
        args |> fst |> Seq.tryHead |> Option.iter(onProjectSelected)

        source.SelectedItem <- null)


  ScrollViewer()
    .Name("ProjectList")
    .Content(
      projects
      |> AVal.map(fun projects ->
        match projects with
        | [] -> emptyProjectsView()
        | projects -> projectsListBox projects)
      |> AVal.toBinding
    )


let viewContent(handleProjectSelected, projects, viewState) : Control =
  let content =
    viewState
    |> AVal.map(fun state ->
      match state with
      | EditProjects ->
        TextBlock().Text("[Edit Projects Dialog Placeholder]") :> Control
      | RemoveProjects ->
        TextBlock().Text("[Remove Projects Dialog Placeholder]")
      | Empty -> repositoryList handleProjectSelected projects)

  Border().Child(content |> AVal.toBinding)

let View
  (vm: LandingVM, logger: ILogger)
  _
  (nav: INavigable<Control>)
  : Control =
  let view = UserControl()
  vm.LoadProjects() |> Async.StartImmediate

  let handleProjectSelected(project: Project) =
    asyncEx {
      let url =
        match project with
        | Local _ -> $"/projects/local/{project.Id.ToString()}"
        | Virtual _ -> $"/projects/virtual/{project.Id.ToString()}"

      match! nav.Navigate url with
      | Ok _ -> ()
      | Error(e) ->
        logger.LogWarning("Navigation Failure: {error}", e.StringError())
    }
    |> Async.StartImmediate

  view
    .Name("Landing")
    .Content(
      GlassCard()
        .Content(viewContent(handleProjectSelected, vm.Projects, vm.ViewState))
    )
