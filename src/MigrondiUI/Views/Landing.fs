module MigrondiUI.Views.Landing

open System
open System.IO
open System.Text.Json

open Microsoft.Extensions.Logging

open IcedTasks

open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Platform.Storage

open NXUI.Extensions

open FSharp.Data.Adaptive
open FsToolkit.ErrorHandling

open Navs
open Navs.Avalonia
open Migrondi.Core
open MigrondiUI
open MigrondiUI.Projects
open MigrondiUI.Components.CreateVirtualProjectView
open MigrondiUI.VirtualFs


type LandingViewState =
  | NewProject
  | EditProjects
  | RemoveProjects
  | Empty

type LocalProjectTarget =
  | CreateLocal
  | ImportToVirtual

[<Struct>]
type ViewContentProps = {
  viewState: LandingViewState aval
  projects: Project list aval
  handleProjectSelected: Project -> unit
  handleSelectLocalProject: LocalProjectTarget -> unit
  handleCreateNewLocalProject: unit -> unit
  handleCreateVirtualProject: NewVirtualProjectArgs -> unit
}

type LandingVM
  (
    logger: ILogger<LandingVM>,
    projects: ILocalProjectRepository,
    vProjects: IVirtualProjectRepository,
    vfs: MigrondiUIFs
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

  member _.LoadLocalProject(view: Control) : Async<Guid option> = asyncOption {
    let! token = Async.CancellationToken
    logger.LogDebug "Loading local project"
    let! topLevel = TopLevel.GetTopLevel(view)

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

    let! file = file |> Seq.tryHead
    let! parentFolder = file.GetParentAsync()
    let! parentFolder = parentFolder
    let name = parentFolder.Name

    logger.LogDebug("Selected file: {File}", file.Name)

    let! pid =
      projects.InsertProject
        (name,
         // TODO: handle uris when we're not on desktop platforms
         configPath = file.Path.LocalPath)
        token

    return pid
  }

  member _.CreateNewLocalProject(view) = asyncOption {
    logger.LogDebug "Creating new local project"
    let! token = Async.CancellationToken
    let! topLevel = TopLevel.GetTopLevel view

    let! directory = asyncOption {
      let! directory =
        topLevel.StorageProvider.OpenFolderPickerAsync(
          FolderPickerOpenOptions(AllowMultiple = false)
        )

      let! selected = directory |> Seq.tryHead

      return! selected.TryGetLocalPath()
    }

    let config = MigrondiConfig.Default

    logger.LogDebug("Selected directory: {Directory}", directory)

    try
      Directory.CreateDirectory(Path.Combine(directory, "migrations")) |> ignore
    with
    | :? IOException
    | :? UnauthorizedAccessException as ex ->

      logger.LogWarning(
        "Failed to create migrations directory: {Message}",
        ex.Message
      )

    let configPath = Path.Combine(directory, "migrondi.json")
    logger.LogDebug("Creating config file in {configfile}", configPath)

    File.WriteAllText(
      configPath,
      Json
        .migrondiConfigEncoder(config)
        .ToJsonString(
          JsonSerializerOptions(WriteIndented = true, IndentSize = 2)
        )
    )

    let! dirName = Path.GetFileNameWithoutExtension directory

    logger.LogDebug("Inserting local project with name {Name}", dirName)

    logger.LogDebug(
      "Inserting local project with config path {Path}",
      directory
    )

    logger.LogDebug("Inserting local project with config {Config}", config)

    let configPath = Path.Combine(directory, "migrondi.json")

    let! pid = projects.InsertProject (dirName, configPath = configPath) token

    logger.LogDebug("Inserted local project with id {Id}", pid)
    return pid
  }

  member _.CreateNewVirtualProject(args: NewVirtualProjectArgs) = asyncEx {
    logger.LogDebug "Creating new virtual project"
    let! pid = vProjects.InsertProject(args)
    logger.LogDebug("Inserted virtual project with id {Id}", pid)
    return pid
  }

  member _.ImportToVirtualProject(view: Control) : Async<Guid option> = asyncOption {
    logger.LogDebug "Importing local project to virtual project"
    let! token = Async.CancellationToken
    let! topLevel = TopLevel.GetTopLevel(view)

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

    let! file = file |> Seq.tryHead
    logger.LogDebug("Selected file: {File}", file.Name)
    let! guid = vfs.ImportFromLocal file.Path.LocalPath token
    return guid
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

let importLocalProject
  (
    handleSelectLocalProject: unit -> unit,
    handleCreateNewLocalProject: unit -> unit
  ) : Control =
  let createNewLocalProject: Control =
    Button()
      .Content("Create New Local Project")
      .OnClickHandler(fun _ _ -> handleCreateNewLocalProject())

  let selectLocalProject: Control =
    Button()
      .Content("Select Local Project")
      .OnClickHandler(fun _ _ -> handleSelectLocalProject())

  StackPanel()
    .Spacing(10)
    .Children(createNewLocalProject, selectLocalProject)
    .Margin(10)

let newProjectView
  (
    handleSelectLocalProject,
    handleCreateNewLocalProject,
    handleCreateVirtualProject
  ) : Control =
  let projectType = cval "Local"

  let comboBox =
    ComboBox()
      .ItemsSource([ "Local"; "Virtual" ])
      .SelectedItem(projectType |> AVal.toBinding)
      .OnSelectionChanged<string>(fun (args, _) ->
        args |> fst |> Seq.tryHead<string> |> Option.iter projectType.setValue)

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
            | "Local" ->
              importLocalProject(
                (fun () -> handleSelectLocalProject(CreateLocal)),
                handleCreateNewLocalProject
              )
            | "Virtual" ->
              CreateVirtualProjectView(
                handleCreateVirtualProject,
                fun () -> handleSelectLocalProject(ImportToVirtual)
              )
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
      | NewProject ->
        newProjectView(
          props.handleSelectLocalProject,
          props.handleCreateNewLocalProject,
          props.handleCreateVirtualProject
        )
      | EditProjects -> TextBlock().Text("[Edit Projects Dialog Placeholder]")
      | RemoveProjects ->
        TextBlock().Text("[Remove Projects Dialog Placeholder]")
      | Empty -> repositoryList props.handleProjectSelected props.projects)

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
        | Local _ -> $"/projects/local/%s{project.Id.ToString()}"
        | Virtual _ -> $"/projects/virtual/%s{project.Id.ToString()}"

      match! nav.Navigate url with
      | Ok _ -> ()
      | Error(e) ->
        logger.LogWarning("Navigation Failure: {error}", e.StringError())
    }
    |> Async.StartImmediate

  let handleSelectLocalProject(target: LocalProjectTarget) = asyncEx {
    let! projectId = asyncEx {
      return!
        match target with
        | CreateLocal -> vm.LoadLocalProject view
        | ImportToVirtual -> vm.ImportToVirtualProject view
    }

    match projectId with
    | None ->
      logger.LogWarning "No project id returned"
      return ()
    | Some projectId ->

      let target =
        match target with
        | CreateLocal -> "local"
        | ImportToVirtual -> "virtual"

      match! nav.Navigate $"/projects/{target}/{projectId}" with
      | Ok _ -> vm.LoadProjects() |> Async.StartImmediate
      | Error(e) ->
        logger.LogWarning("Navigation Failure: {error}", e.StringError())
  }

  let handleCreateNewLocalProject() = asyncEx {
    let! projectId = vm.CreateNewLocalProject view

    match projectId with
    | None ->
      logger.LogWarning "No project id returned"
      return ()
    | Some projectId ->


      match! nav.Navigate $"/projects/local/%s{projectId.ToString()}" with
      | Ok _ -> ()
      | Error(e) ->
        logger.LogWarning("Navigation Failure: {error}", e.StringError())

      vm.SetLandingState Empty
      vm.LoadProjects() |> Async.StartImmediate
  }

  let viewContentProps = {
    viewState = vm.ViewState
    projects = vm.Projects
    handleProjectSelected = handleProjectSelected
    handleSelectLocalProject =
      fun target -> handleSelectLocalProject target |> Async.StartImmediate
    handleCreateNewLocalProject =
      fun target -> handleCreateNewLocalProject target |> Async.StartImmediate
    handleCreateVirtualProject =
      fun args ->
        asyncEx {
          let! createdId = vm.CreateNewVirtualProject args
          do vm.SetLandingState Empty

          match! nav.Navigate $"/projects/virtual/{createdId}" with
          | Ok _ -> vm.LoadProjects() |> Async.StartImmediate
          | Error(e) ->
            logger.LogWarning("Navigation Failure: {error}", e.StringError())
        }
        |> Async.StartImmediate
  }


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
