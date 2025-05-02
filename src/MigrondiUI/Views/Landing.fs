namespace MigrondiUI.Views

open System

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Avalonia.Controls

open NXUI.Extensions

open FSharp.Data.Adaptive

open Navs
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Projects

module Landing =
  open Avalonia.Controls.Templates
  open System.Collections.Generic
  open Microsoft.Extensions.Logging

  type LandingViewState =
    | NewProject
    | EditProjects
    | RemoveProjects
    | Empty

  let inline localProjectTemplate(project: LocalProject) =

    StackPanel()
      .Children(
        TextBlock().Text(project.name),
        TextBlock()
          .Text(project.description |> Option.defaultValue "No description")
      )
      .Spacing(5)
      .Margin(5)
      .OrientationVertical()

  let repositoryItem =
    FuncDataTemplate<Project>(fun project _ ->
      StackPanel()
        .Tag(project.Id)
        .Children(
          TextBlock().Text project.Name,
          TextBlock().Text(defaultArg project.Description "No description")
        )
        .Spacing(5)
        .Margin(5)
        .OrientationVertical())

  // Component for empty state
  let emptyProjectsView() : Control =
    StackPanel()
      .Children(TextBlock().Text("No projects available"))
      .Spacing(5)
      .Margin(5)
      .OrientationVertical()

  // Component for the ListBox of projects
  let projectsListBox onProjectSelected (projects: Project list) : Control =
    let lb =
      ListBox()
        .ItemsSource(projects)
        .ItemTemplate(repositoryItem)
        .OnSelectionChangedHandler(fun _ args ->
          if args.AddedItems.Count > 0 then
            match args.AddedItems.Item 0 with
            | :? Project as project -> onProjectSelected project
            | _ -> ()
          else
            ())

    lb.SelectionMode <- SelectionMode.Single

    StackPanel()
      .Children(TextBlock().Text("Available projects:"), lb)
      .Spacing(5)
      .Margin(5)
      .OrientationVertical()

  // Main repository list component
  let repositoryList onProjectSelected (projects: Project list aval) : Control =
    let listContent =
      projects
      |> AVal.map(fun projects ->
        match projects with
        | [] -> emptyProjectsView()
        | projects -> projectsListBox onProjectSelected projects)

    ScrollViewer().Name("ProjectList").Content(listContent |> AVal.toBinding)

  // Toolbar with a "New Project" button
  let toolbar selectNewProject : Control =
    StackPanel()
      .OrientationHorizontal()
      .Spacing(10)
      .Children(
        Button()
          .Content("New Project")
          .OnClickHandler(fun _ _ -> selectNewProject NewProject)
      )

  let viewContent
    (viewState: aval<LandingViewState>, projects, handleProjectSelected)
    : Control =
    let content =
      viewState
      |> AVal.map(fun state ->
        match state with
        | NewProject ->
          TextBlock().Text("[New Project Dialog Placeholder]") :> Control
        | EditProjects ->
          TextBlock().Text("[Edit Projects Dialog Placeholder]")
        | RemoveProjects ->
          TextBlock().Text("[Remove Projects Dialog Placeholder]")
        | Empty -> repositoryList handleProjectSelected projects)

    Border().Child(content |> AVal.toBinding)

  type LandingVM(logger: ILogger<LandingVM>, projects: IProjectRepository) =

    let _projects = cval []

    let viewState = cval Empty

    do logger.LogDebug "LandingVM created"

    member _.Projects: Project list aval = _projects

    member _.LoadProjects() = async {
      let! projects = projects.GetProjects()
      _projects.setValue projects
      return ()
    }

    member _.SetLandingState(state: LandingViewState) =
      logger.LogDebug("Setting landing state to {State}", state)
      viewState.setValue state

    member _.EmptyViewState() =
      logger.LogDebug("Setting landing state to Empty")
      viewState.setValue Empty

    member _.ViewState: aval<LandingViewState> = viewState

  let View (vm: LandingVM) _ (nav: INavigable<Control>) : Async<Control> = async {
    vm.LoadProjects() |> Async.StartImmediate

    let handleProjectSelected(project: Project) =
      async {
        match! nav.Navigate $"/projects/%s{project.Id.ToString()}" with
        | Ok _ -> ()
        | Error(NavigationFailed e) -> printfn "Navigation failed: %s" e
        | _ -> printfn "Unknown navigation error"

      }
      |> Async.StartImmediate

    return
      UserControl()
        .Name("Landing")
        .Content(
          DockPanel()
            .LastChildFill(true)
            .Children(
              toolbar(vm.SetLandingState).DockTop(),
              viewContent(vm.ViewState, vm.Projects, handleProjectSelected)
                .DockTop()
            )
            .Margin(10)
        )
      :> Control
  }
