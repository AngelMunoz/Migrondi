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
open MigrondiUI.Views.Components


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
  handleCreateNewLocalProject: unit -> unit
  handleCreateVirtualProject: Projects.NewVirtualProjectArgs -> unit
}

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

  member _.LoadLocalProject(view: Control) : Async<Guid voption> = asyncEx {
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
          projects.InsertProject(
            parentFolder.Name,
            // TODO: handle uris when we're not on desktop platforms
            configPath = file.Path.LocalPath
          )

        return ValueSome pid
      | None ->
        logger.LogWarning "No file selected"
        return ValueNone
  }

  member _.CreateNewLocalProject(view) = asyncEx {
    logger.LogDebug "Creating new local project"

    match TopLevel.GetTopLevel view with
    | null ->
      logger.LogWarning "TopLevel is null"
      return ValueNone
    | topLevel ->
      let! selectedDirectory = asyncOption {
        let! directory =
          topLevel.StorageProvider.OpenFolderPickerAsync(
            FolderPickerOpenOptions(AllowMultiple = false)
          )

        let! selected = directory |> Seq.tryHead

        return! selected.TryGetLocalPath() |> Option.ofNull
      }

      match selectedDirectory with
      | None ->
        logger.LogWarning "No directory selected"
        return ValueNone
      | Some directory ->
        let config = MigrondiConfig.Default

        logger.LogDebug("Selected directory: {Directory}", directory)

        try
          Directory.CreateDirectory(Path.Combine(directory, "migrations"))
          |> ignore
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

        let dirName = Path.GetFileNameWithoutExtension directory |> nonNull

        logger.LogDebug("Inserting local project with name {Name}", dirName)

        logger.LogDebug(
          "Inserting local project with config path {Path}",
          directory
        )

        logger.LogDebug("Inserting local project with config {Config}", config)

        let configPath = Path.Combine(directory, "migrondi.json")

        let! pid = projects.InsertProject(dirName, configPath = configPath)

        logger.LogDebug("Inserted local project with id {Id}", pid)
        return ValueSome pid
  }

  member _.CreateNewVirtualProject(args: Projects.NewVirtualProjectArgs) = asyncEx {
    logger.LogDebug "Creating new virtual project"
    let! pid = vProjects.InsertProject(args)
    logger.LogDebug("Inserted virtual project with id {Id}", pid)
    return pid
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

let createVirtualProject
  (onCreateVirtualProject: Projects.NewVirtualProjectArgs -> unit)
  : Control =
  // State for form fields
  let name = cval ""
  let description = cval ""
  let connection = cval "Data Source=./migrondi.db"
  let driver = cval MigrondiDriver.Sqlite

  let driverTpl =
    FuncDataTemplate<MigrondiDriver>(fun driver _ ->
      TextBlock().Text(driver.AsString))

  let driverOptions = [
    MigrondiDriver.Sqlite
    MigrondiDriver.Postgresql
    MigrondiDriver.Mysql
    MigrondiDriver.Mssql
  ]

  let driverCombo =
    ComboBox()
      .ItemsSource(driverOptions)
      .SelectedIndex(0)
      .ItemTemplate(driverTpl)
      .OnSelectionChanged<MigrondiDriver>(fun (args, _) ->
        args |> fst |> Seq.tryHead |> Option.iter(AVal.setValue driver))

  let form =
    let nameTextBox =
      TextBox()
        .Text(name.Value)
        .OnTextChangedHandler(fun tb _ -> name.setValue tb.Text)

    let descriptionTextBox =
      TextBox()
        .Text(description.Value)
        .OnTextChangedHandler(fun tb _ -> description.setValue tb.Text)

    let connectionTextBox =
      TextBox()
        .Text(connection.Value)
        .OnTextChangedHandler(fun tb _ -> connection.setValue tb.Text)

    let createBtn =
      Button()
        .Content("Create")
        .IsEnabled(
          (name, connection)
          ||> AVal.map2(fun n c -> n.Trim() <> "" && c.Trim() <> "")
          |> AVal.toBinding
        )
        // No-op for now, just a placeholder for submit
        .OnClickHandler(fun _ _ ->
          onCreateVirtualProject {
            name = name.getValue()
            description = description.getValue()
            connection = connection.getValue()
            driver = driver.getValue()
            tableName = MigrondiConfig.Default.tableName
          })

    Grid()
      .Classes("CreateVirtualProjectForm")
      .RowDefinitions("Auto,Auto,Auto,Auto")
      .ColumnDefinitions("*,*")
      .VerticalAlignmentTop()
      .Children(
        LabeledField
          .Vertical("Project Name:", nameTextBox)
          .MarginRight(4)
          .Row(0)
          .Column(0),
        LabeledField
          .Vertical("Description:", descriptionTextBox)
          .MarginLeft(4)
          .Row(0)
          .Column(1),
        LabeledField
          .Vertical("Driver:", driverCombo.HorizontalAlignmentStretch())
          .HorizontalAlignmentStretch()
          .Row(1)
          .Column(0)
          .ColumnSpan(2),
        LabeledField
          .Vertical("Connection String:", connectionTextBox)
          .Row(2)
          .Column(0)
          .ColumnSpan(2),
        createBtn
          .Row(3)
          .Column(1)
          .ColumnSpan(2)
          .HorizontalAlignmentRight()
          .MarginY(10)
      )

  form

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
                handleSelectLocalProject,
                handleCreateNewLocalProject
              )
            | "Virtual" -> createVirtualProject handleCreateVirtualProject
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
  : Async<Control> =
  asyncEx {
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

    let handleSelectLocalProject() = asyncEx {
      let! projectId = vm.LoadLocalProject view
      do vm.SetLandingState Empty

      vm.LoadProjects() |> Async.StartImmediate

      match! nav.Navigate $"/projects/local/%s{projectId.ToString()}" with
      | Ok _ -> ()
      | Error(e) ->
        logger.LogWarning("Navigation Failure: {error}", e.StringError())
    }

    let handleCreateNewLocalProject() = asyncEx {

      let! projectId = vm.CreateNewLocalProject view

      match projectId with
      | ValueNone ->
        logger.LogWarning "No project id returned"
        return ()
      | ValueSome projectId ->


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
        fun () -> handleSelectLocalProject() |> Async.StartImmediate
      handleCreateNewLocalProject =
        fun () -> handleCreateNewLocalProject() |> Async.StartImmediate
      handleCreateVirtualProject =
        fun args ->
          asyncEx {
            let! createdId = vm.CreateNewVirtualProject args
            do vm.SetLandingState Empty

            match! nav.Navigate $"/projects/virtual/%O{createdId}" with
            | Ok _ -> ()
            | Error(e) ->
              logger.LogWarning("Navigation Failure: {error}", e.StringError())
          }
          |> Async.StartImmediate
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
