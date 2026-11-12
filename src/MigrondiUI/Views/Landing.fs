module MigrondiUI.Views.Landing

open System

open Microsoft.Extensions.Logging

open Avalonia.Controls
open Avalonia.Controls.Templates

open NXUI.Extensions

open FSharp.Data.Adaptive

open Navs
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Services
open MigrondiUI.Logic
open MigrondiUI.Logic.Landing
open SukiUI.Controls

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
  (logger: ILogger<LandingVM>)
  (projects: IProjectCollection)
  _
  (nav: INavigable<Control>)
  : Control =
  let view = UserControl()
  let navigator = AvaloniaNavigator nav
  let vm = LandingVM(logger, projects, navigator)
  vm.LoadProjects() |> Async.StartImmediate

  view
    .Name("Landing")
    .Content(
      GlassCard()
        .Content(
          viewContent(
            (fun project -> vm.OpenProject project |> Async.StartImmediate),
            vm.Projects,
            vm.ViewState
          )
        )
    )
