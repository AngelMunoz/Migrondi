module Migrondi.UI.Views.NewProject

open System
open System.IO

open Goo
open Goo.Widgets
open Goo.Widgets.Inputs
open Goo.Widgets.Navigation
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style
open FunGoo.Widgets

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Navs

open Migrondi.Core
open Migrondi.Core.Serialization
open MigrondiUI
open MigrondiUI.Services
open Migrondi.UI.Logic
open Migrondi.UI.Types
open Migrondi.UI.Views.Common

// The page state: one mutable field per concern. The cell reads it inside
// Build; the transition functions below replace whole values.
type NewProjectState = {
  mutable Name: string
  mutable Description: string
  mutable Connection: string
  mutable TableName: string
  mutable Driver: string
  mutable ConfigPath: string
  mutable Notice: string
  mutable Busy: bool
  mutable Tab: string
}

// One event channel for the page's actions; the cell dispatches it. Text
// fields write their copy of the value straight into the state.
type NewProjectEvent =
  | Back
  | BrowseConfig
  | CreateLocal
  | CreateVirtual
  | Import
  | SelectTab of string

// State transitions: plain functions, callable without a window.
let created(state: NewProjectState, name: string) =
  state.Name <- ""
  state.Description <- ""
  state.Connection <- ""
  state.Notice <- $"Created {name}"
  state.Busy <- false

let imported(state: NewProjectState) =
  state.ConfigPath <- ""
  state.Notice <- "Imported project"
  state.Busy <- false

let failed(state: NewProjectState, context: string, ex: exn) =
  state.Notice <- $"{context} failed: {ex.Message}"
  state.Busy <- false

// What an operation needs: the cell to rebuild, the marshaller to return
// through, the state to write, the services to drive, and the router for
// the follow-up navigation. One carrier so the operations below stay
// one-parameter functions.
type Operations = {
  Cell: Cell
  PostUI: IPostUI
  Feedback: IFeedback
  Picker: IFilePicker
  Projects: IProjectCollection
  Navigator: IRouter<Blob>
  State: NewProjectState
}

// Background operations: the cell arrives as a parameter, so these stay
// plain functions and the object expression stays non-recursive.
// Creates a local project on disk: picks a folder, writes the default
// config with a migrations folder, and registers it. The picker needs the
// UI thread, so this starts immediate and continues on a worker.
let createLocal(operations: Operations) : unit =
  asyncEx {
    let! picked = operations.Picker.PickDirectory()

    match picked with
    | None -> ()
    | Some directory ->
      try
        let config = MigrondiConfig.Default

        Directory.CreateDirectory(Path.Combine(directory, "migrations"))
        |> ignore

        let configPath = Path.Combine(directory, "migrondi.json")

        File.WriteAllText(configPath, MiSerializer.Encode config)

        let! projectId = operations.Projects.RegisterLocal configPath

        operations.PostUI.Post(fun () ->
          operations.State.Notice <- "Created local project"
          operations.State.Busy <- false
          operations.Feedback.Toast "Created local project"
          operations.Cell.Rebuild())

        Navigation.go(operations.Navigator, Routes.localProjectUrl projectId)
        |> ignore
      with ex ->
        operations.PostUI.Post(fun () ->
          failed(operations.State, "Creating the local project", ex)
          operations.Feedback.Toast $"Create failed: {ex.Message}"
          operations.Cell.Rebuild())
  }
  |> Async.StartImmediate

// Runs on the UI thread from the Build-authored callback: the picker needs
// it to start the native chooser. The await part continues on a worker.
let browse(operations: Operations) : unit =
  asyncEx {
    let! picked = operations.Picker.PickProjectConfig()

    match picked with
    | Some path ->
      operations.PostUI.Post(fun () ->
        operations.State.ConfigPath <- path
        operations.Cell.Rebuild())
    | None -> ()
  }
  |> Async.StartImmediate

let createVirtual(operations: Operations) : unit =
  asyncEx {
    let state = operations.State

    if
      String.IsNullOrWhiteSpace state.Name
      || String.IsNullOrWhiteSpace state.Connection
    then
      state.Notice <- "Name and connection string are required"
    else
      state.Busy <- true

      try
        let name = state.Name.Trim()
        let description = state.Description.Trim()

        let args: Database.InsertVirtualProjectArgs = {
          name = name
          description =
            if String.IsNullOrWhiteSpace description then
              None
            else
              Some description
          connection = state.Connection.Trim()
          tableName = state.TableName.Trim()
          driver = state.Driver
        }

        let! projectId = operations.Projects.CreateVirtual args

        operations.PostUI.Post(fun () ->
          created(state, name)
          operations.Feedback.Toast $"Created {name}"
          operations.Cell.Rebuild())

        Navigation.go(operations.Navigator, Routes.virtualProjectUrl projectId)
        |> ignore
      with ex ->
        operations.PostUI.Post(fun () ->
          failed(state, "Creating the project", ex)
          operations.Feedback.Toast $"Create failed: {ex.Message}"
          operations.Cell.Rebuild())
  }
  |> Async.Start

let importLocal(operations: Operations) : unit =
  asyncEx {
    let path = operations.State.ConfigPath.Trim()

    if String.IsNullOrWhiteSpace path then
      operations.State.Notice <-
        "Pick or type the path to a migrondi.json first"
    else
      operations.State.Busy <- true

      try
        let! projectId = operations.Projects.Import path

        operations.PostUI.Post(fun () ->
          imported(operations.State)
          operations.Feedback.Toast "Imported project"
          operations.Cell.Rebuild())

        Navigation.go(operations.Navigator, Routes.virtualProjectUrl projectId)
        |> ignore
      with ex ->
        operations.PostUI.Post(fun () ->
          failed(operations.State, "Importing", ex)
          operations.Feedback.Toast $"Import failed: {ex.Message}"
          operations.Cell.Rebuild())
  }
  |> Async.Start

// The field cells the form mounts: each owns its draft, so typing never
// repaints the page. Created once at cell birth and pushed the state's
// value on every build, so cleared forms and picker results land.
type Fields = {
  Name: TextFieldCell
  Description: TextFieldCell
  Connection: TextFieldCell
  TableName: TextFieldCell
  ConfigPath: TextFieldCell
}

let fieldsFor(state: NewProjectState) : Fields = {
  Name = textField("Payroll service", "", fun value -> state.Name <- value)
  Description =
    textField(
      "What this project tracks",
      "",
      fun value -> state.Description <- value
    )
  Connection =
    textField(
      "Data Source=./migrondi.db",
      "",
      fun value -> state.Connection <- value
    )
  TableName =
    textField("", state.TableName, fun value -> state.TableName <- value)
  ConfigPath =
    textField(
      "Path to a migrondi.json",
      "",
      fun value -> state.ConfigPath <- value
    )
}

let fieldRow(palette: Palette, label: string, child: Blob) : Blob =
  Container(FlexDirection = FlexDirection.Column, Gap = 6, MinWidth = 0)
    .Children(fieldLabel(palette, label), child)

// One form surface for every creation path: same width, same rhythm, so the
// three flows read as one screen with three modes instead of three screens.
let formCard
  (palette: Palette, title: string, description: string, body: Blob array)
  : Blob =
  let heading: Blob =
    Container(FlexDirection = FlexDirection.Column, Gap = 4)
      .Children(
        Text(
          Content = title,
          FontSize = 15,
          Color = palette.Text,
          FontWeight = 600
        ),
        Text(Content = description, FontSize = 12, Color = palette.TextMuted)
      )

  Container(
    FlexDirection = FlexDirection.Column,
    Gap = 16,
    Padding = 20,
    Width = 640.0,
    BackgroundColor = palette.Panel,
    BorderRadius = 10
  )
    .Children
    [| heading; yield! body |]

// The action row every form ends with: the notice on the left, the single
// primary action on the right.
let formActions
  (
    palette: Palette,
    state: NewProjectState,
    label: string,
    onEvent: NewProjectEvent -> unit
  ) : Blob =
  Container(
    FlexDirection = FlexDirection.Row,
    Gap = 8,
    AlignItems = AlignItems.Center
  )
    .Children(
      Text(
        Content = state.Notice,
        FontSize = 12,
        Color = palette.TextMuted,
        FlexGrow = 1.0
      ),
      primaryButton(palette, label, state.Busy, fun () -> onEvent CreateVirtual)
    )

// The driver picker is a mounted widget: it mounts under one stable key and
// takes the delegate created once at cell birth, so an unchanged input
// skips the rebuild.
let driverPicker
  (key: string, state: NewProjectState, onDriverSelect: Action<string>)
  : Blob =
  let input =
    ComboBoxInput(
      Items = [|
        ComboBoxOption(Id = "sqlite", Label = "sqlite")
        ComboBoxOption(Id = "mssql", Label = "mssql (SQL Server)")
        ComboBoxOption(Id = "postgresql", Label = "postgresql")
        ComboBoxOption(Id = "mysql", Label = "mysql")
      |],
      SelectedId = state.Driver,
      OnSelect = onDriverSelect
    )

  Cell.Mount<ComboBoxInput, ComboBox>(key, input)

let virtualTab
  (
    palette: Palette,
    state: NewProjectState,
    fields: Fields,
    onDriverSelect: Action<string>,
    onEvent: NewProjectEvent -> unit
  ) : Blob =
  formCard(
    palette,
    "Virtual project",
    "Stored in this app's own database. No files on disk, migrations included.",
    [|
      Container(FlexDirection = FlexDirection.Row, Gap = 12)
        .Children(
          Container(FlexGrow = 1.0, MinWidth = 0)
            .Children(
              fieldRow(
                palette,
                "Project name",
                mountField("new-project-name", fields.Name, state.Name)
              )
            ),
          Container(FlexGrow = 1.0, MinWidth = 0)
            .Children(
              fieldRow(
                palette,
                "Description (optional)",
                mountField(
                  "new-project-description",
                  fields.Description,
                  state.Description
                )
              )
            )
        )
      fieldRow(
        palette,
        "Connection string",
        mountField(
          "new-project-connection",
          fields.Connection,
          state.Connection
        )
      )
      Container(FlexDirection = FlexDirection.Row, Gap = 12)
        .Children(
          Container(FlexGrow = 1.0, MinWidth = 0)
            .Children(
              fieldRow(
                palette,
                "Driver",
                driverPicker("new-project-driver", state, onDriverSelect)
              )
            ),
          Container(FlexGrow = 1.0, MinWidth = 0)
            .Children(
              fieldRow(
                palette,
                "Tracking table",
                mountField(
                  "new-project-table",
                  fields.TableName,
                  state.TableName
                )
              )
            )
        )
      :> Blob
      formActions(palette, state, "Create project", onEvent)
    |]
  )

let importTab
  (
    palette: Palette,
    state: NewProjectState,
    fields: Fields,
    onEvent: NewProjectEvent -> unit
  ) : Blob =
  let actions =
    Container(
      FlexDirection = FlexDirection.Row,
      Gap = 8,
      AlignItems = AlignItems.Center
    )
      .Children(
        Text(
          Content = state.Notice,
          FontSize = 12,
          Color = palette.TextMuted,
          FlexGrow = 1.0
        ),
        actionButton(palette, "Browse…", fun () -> onEvent BrowseConfig),
        primaryButton(
          palette,
          "Import project",
          state.Busy,
          fun () -> onEvent Import
        )
      )

  formCard(
    palette,
    "Import from disk",
    "Reads an existing migrondi.json and its migrations into a virtual project.",
    [|
      fieldRow(
        palette,
        "migrondi.json path",
        mountField("new-project-config", fields.ConfigPath, state.ConfigPath)
      )
      actions
    |]
  )

let localTab(palette: Palette, onEvent: NewProjectEvent -> unit) : Blob =
  formCard(
    palette,
    "New project on disk",
    "Picks a folder, writes migrondi.json and a migrations folder, then registers it.",
    [|
      Container(FlexDirection = FlexDirection.Row, Gap = 8)
        .Children(
          primaryButton(
            palette,
            "Choose folder…",
            false,
            fun () -> onEvent CreateLocal
          )
        )
      :> Blob
    |]
  )

let page
  (
    palette: Palette,
    state: NewProjectState,
    fields: Fields,
    onDriverSelect: Action<string>,
    onTabSelect: Action<string>,
    onEvent: NewProjectEvent -> unit
  ) : Blob =
  let tabs =
    Cell.Mount<TabBarInput, TabBar>(
      "new-project-tabs",
      TabBarInput(
        Items = [|
          NavigationItem(Id = "virtual", Label = "Virtual")
          NavigationItem(Id = "import", Label = "From disk")
          NavigationItem(Id = "local", Label = "New on disk")
        |],
        SelectedId = state.Tab,
        OnSelect = onTabSelect
      )
    )

  let form =
    match state.Tab with
    | "import" -> importTab(palette, state, fields, onEvent)
    | "local" -> localTab(palette, onEvent)
    | _ -> virtualTab(palette, state, fields, onDriverSelect, onEvent)

  Container(
    Key = "new-project",
    FlexDirection = FlexDirection.Column,
    Gap = 20,
    Padding = 24,
    Width = percent 100.0,
    Height = percent 100.0,
    BackgroundColor = palette.Canvas
  )
    .Children(
      pageHeader(
        palette,
        "New project",
        "Create a virtual project, import one from disk, or scaffold a new folder",
        [| actionButton(palette, "< Back", fun () -> onEvent Back) |]
      ),
      tabs,
      form
    )

// The page owns its form state (state ladder tier 2). It carries no load
// to kick, so the cell exposes no Start member.
let create<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideTheme
  and 'env :> IProvideFeedback
  and 'env :> IProvideFilePicker
  and 'env :> IProvideNavigator>
  (env: 'env)
  : Cell =
  let state = {
    Name = ""
    Description = ""
    Connection = ""
    TableName = MigrondiConfig.Default.tableName
    Driver = "sqlite"
    ConfigPath = ""
    Notice = ""
    Busy = false
    Tab = "virtual"
  }

  // Created once at cell birth: the mounted driver picker and tab bar keep
  // their delegates, and the fields keep their drafts.
  let onDriverSelect = Action<string>(fun id -> state.Driver <- id)
  let onTabSelect = Action<string>(fun id -> state.Tab <- id)

  let fields = fieldsFor state

  {
    new Cell() with
      override this.Build() : Blob =
        let palette = Palette.forTheme env.Theme.Store.Dark

        let operations: Operations = {
          Cell = this
          PostUI = env.PostUI
          Feedback = env.Feedback
          Picker = env.FilePicker
          Projects = env.Projects
          Navigator = env.Navigator
          State = state
        }

        let onEvent =
          function
          | Back -> Navigation.go(env.Navigator, "/")
          | BrowseConfig -> browse operations
          | CreateLocal -> createLocal operations
          | CreateVirtual -> createVirtual operations
          | Import -> importLocal operations
          | SelectTab _ -> this.Rebuild()

        page(palette, state, fields, onDriverSelect, onTabSelect, onEvent)
  }
