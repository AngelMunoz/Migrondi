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

  let repositoryList onProjectSelected (projects: Project list aval) =
    let lb =
      ListBox()
        .ItemsSource(projects |> AVal.toBinding)
        .ItemTemplate(repositoryItem)
        .OnSelectionChangedHandler(fun _ args ->
          if args.AddedItems.Count > 0 then
            match args.AddedItems.Item 0 with
            | :? Project as project -> onProjectSelected project
            | _ -> ()
          else
            ())

    lb.SelectionMode <- SelectionMode.Single
    ScrollViewer().Name("ProjectList").Content(lb)

  type LandingVM(projects: IProjectRepository) =

    let _projects = cval []
    member this.Projects: Project list aval = _projects

    member _.LoadProjects() = async {
      let! projects = projects.GetProjects()
      _projects.setValue projects
      return ()
    }

  let View (vm: LandingVM) _ (nav: INavigable<Control>) : Async<Control> = async {
    vm.LoadProjects() |> Async.StartImmediate

    let handleProjectSelected(project: Project) =
      nav.Navigate $"/projects/%s{project.Id.ToString()}"
      |> Async.AwaitTask
      |> Async.Ignore
      |> Async.StartImmediate
      // Handle the selected project here
      printfn $"Selected project: %s{project.Name}"

    return
      UserControl()
        .Name("Landing")
        .Content(
          StackPanel()
            .Children(
              TextBlock().Text("Welcome to Migrondi!"),
              repositoryList handleProjectSelected vm.Projects
            )
            .Spacing(10)
            .Margin(10)
            .OrientationVertical()
        )
      :> Control
  }
