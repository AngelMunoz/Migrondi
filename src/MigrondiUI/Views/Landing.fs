namespace MigrondiUI.Views

open System

open Microsoft.Extensions.Logging

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Platform.Storage

open NXUI.Extensions

open FSharp.Data.Adaptive

open Navs
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Projects


module Landing =

  type LandingViewState =
    | NewProject
    | EditProjects
    | RemoveProjects
    | Empty

  [<Struct>]
  type ViewContentProps = {
    viewState: LandingViewState aval
    projects: Project list aval
    handleProjectSelected: Project -> unit
    handleSelectLocalProject: unit -> unit
  }

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
      logger.LogDebug "Setting landing state to Empty"
      viewState.setValue Empty

    member _.ViewState: aval<LandingViewState> = viewState

    member _.LoadLocalProject(view: Control) : Async<Guid voption> = async {
      logger.LogDebug "Loading local project"

      match TopLevel.GetTopLevel(view) with
      | null ->
        logger.LogWarning "TopLevel is null"
        return ValueNone
      | topLevel ->
        let! file =
          topLevel.StorageProvider.OpenFilePickerAsync(
            FilePickerOpenOptions(
              Title = "Select Project File",
              AllowMultiple = false,
              FileTypeFilter = [|
                FilePickerFileType(
                  "Migrondi Config",
                  Patterns = [ "migrondi.json" ]
                )
              |]
            )
          )

        match file |> Seq.tryHead with
        | Some file ->
          let! parentFolder = file.GetParentAsync()

          logger.LogDebug("Selected file: {File}", file.Name)

          let! pid =
            projects.InsertLocalProject(
              parentFolder.Name,
              // TODO: handle uris when we're not on desktop platforms
              configPath = file.Path.LocalPath
            )

          return ValueSome pid
        | None ->
          logger.LogWarning "No file selected"
          return ValueNone
    }

  let inline emptyProjectsView() : Control =
    StackPanel()
      .Children(TextBlock().Text("No projects available"))
      .Spacing(5)
      .Margin(5)
      .OrientationVertical()

  let repositoryList onProjectSelected (projects: Project list aval) : Control =
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

  let toolbar(viewState, setLandingState) : Control =
    StackPanel()
      .OrientationHorizontal()
      .Spacing(10)
      .Children(
        ContentControl()
          .Content(
            viewState
            |> AVal.map(fun state ->
              match state with
              | Empty ->
                Button()
                  .Content("New Project")
                  .OnClickHandler(fun _ _ -> setLandingState NewProject)
              | _ ->
                Button()
                  .Content("Back")
                  .OnClickHandler(fun _ _ -> setLandingState Empty))
            |> AVal.toBinding
          )
      )

  let importLocalProject(handleSelectLocalProject: unit -> unit) : Control =
    Button()
      .Content("Select Local Project")
      .OnClickHandler(fun _ _ -> handleSelectLocalProject())

  let createVirtualProject() : Control = UserControl()

  let newProjectView(handleSelectLocalProject: unit -> unit) : Control =
    let projectType = cval "Local"

    let comboBox =
      ComboBox()
        .ItemsSource([ "Local"; "Virtual" ])
        .SelectedItem(projectType |> AVal.toBinding)
        .OnSelectionChanged<string>(fun (args, _) ->
          args
          |> fst
          |> Seq.tryHead<string>
          |> Option.iter projectType.setValue)

    Grid()
      .RowDefinitions("Auto,Auto")
      .ColumnDefinitions("250,*")
      .Children(
        TextBlock().Text("Select Project Type").Row(0).Column(0),
        comboBox.Row(1).Column(0),
        Border()
          .Child(
            projectType
            |> AVal.map(fun t ->
              match t with
              | "Local" -> TextBlock().Text("Import Local Project")
              | "Virtual" -> TextBlock().Text("Create Virtual Project")
              | _ -> TextBlock().Text "Unknown project type")
            |> AVal.toBinding
          )
          .Row(0)
          .Column(1),
        Border()
          .Child(
            projectType
            |> AVal.map(fun t ->
              match t with
              | "Local" -> importLocalProject handleSelectLocalProject
              | "Virtual" -> createVirtualProject()
              | _ -> TextBlock().Text "Unknown project type")
            |> AVal.toBinding
          )
          .Row(1)
          .Column(1)

      )
      .Margin(10)

  let viewContent(props: ViewContentProps) : Control =
    let content =
      props.viewState
      |> AVal.map(fun state ->
        match state with
        | NewProject -> newProjectView props.handleSelectLocalProject
        | EditProjects ->
          TextBlock().Text("[Edit Projects Dialog Placeholder]")
        | RemoveProjects ->
          TextBlock().Text("[Remove Projects Dialog Placeholder]")
        | Empty -> repositoryList props.handleProjectSelected props.projects)

    Border().Child(content |> AVal.toBinding)

  let View
    (vm: LandingVM, logger: ILogger)
    _
    (nav: INavigable<Control>)
    : Async<Control> =
    async {
      let view = UserControl()
      vm.LoadProjects() |> Async.StartImmediate

      let handleProjectSelected(project: Project) =
        async {
          match! nav.Navigate $"/projects/%s{project.Id.ToString()}" with
          | Ok _ -> ()
          | Error(NavigationFailed e) ->
            logger.LogWarning("Navigation failed: {error}", e)
          | err -> logger.LogError("Unknown navigation error: {error}", err)
        }
        |> Async.StartImmediate

      let handleSelectLocalProject() = async {
        let! projectId = vm.LoadLocalProject view
        do vm.SetLandingState Empty

        vm.LoadProjects() |> Async.StartImmediate

        match! nav.Navigate $"/projects/%s{projectId.ToString()}" with
        | Ok _ -> ()
        | Error(NavigationFailed e) ->
          logger.LogWarning("Navigation failed: {error}", e)
        | err -> logger.LogError("Unknown navigation error: {error}", err)
      }

      let viewContentProps = {
        viewState = vm.ViewState
        projects = vm.Projects
        handleProjectSelected = handleProjectSelected
        handleSelectLocalProject =
          fun () -> handleSelectLocalProject() |> Async.StartImmediate
      }

      return
        view
          .Name("Landing")
          .Content(
            DockPanel()
              .LastChildFill(true)
              .Children(
                toolbar(vm.ViewState, vm.SetLandingState).DockTop(),
                viewContent(viewContentProps).DockTop()
              )
              .Margin(10)
          )
        :> Control
    }
