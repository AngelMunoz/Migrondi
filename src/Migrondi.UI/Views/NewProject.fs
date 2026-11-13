module Migrondi.UI.Views.NewProject

open System

open Goo
open Goo.Widgets
open Goo.Widgets.Inputs
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style
open FunGoo.Widgets

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Navs

open Migrondi.Core
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
}

// One event channel for the page's actions; the cell dispatches it. Text
// fields write their copy of the value straight into the state.
type NewProjectEvent =
  | Back
  | BrowseConfig
  | CreateVirtual
  | Import

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

// View pieces: plain functions over the palette and state, one event
// callback each.
let headerBar(palette: Palette, onEvent: NewProjectEvent -> unit) : Blob =
  Container(
    Key = "new-project-header",
    FlexDirection = FlexDirection.Row,
    Gap = 8,
    AlignItems = AlignItems.Center
  )
    .Children(
      Text(
        Key = "new-project-title",
        Content = "New project",
        FontSize = 20,
        Color = palette.Text,
        FlexGrow = 1.0
      ),
      actionButton(palette, "< Back", fun () -> onEvent Back)
    )

let fieldRow(palette: Palette, label: string, child: Blob) : Blob =
  Container(
    FlexDirection = FlexDirection.Column,
    Gap = 4,
    FlexGrow = 1.0,
    MinWidth = 0
  )
    .Children(
      Text(Content = label, FontSize = 12, Color = palette.TextMuted),
      child
    )

let importBar
  (palette: Palette, state: NewProjectState, onEvent: NewProjectEvent -> unit)
  : Blob =
  Container(
    Key = "new-project-import",
    FlexDirection = FlexDirection.Column,
    Gap = 8,
    Padding = 12,
    BackgroundColor = palette.Panel,
    BorderRadius = 8,
    BorderWidth = edgesFromFloat 1.0,
    BorderColor = palette.Border
  )
    .Children(
      Text(
        Content = "Import a local project into a virtual one",
        FontSize = 14,
        Color = palette.Text
      ),
      Container(FlexDirection = FlexDirection.Row, Gap = 8)
        .Children(
          Container(FlexGrow = 1.0, MinWidth = 0)
            .Children(
              TextField(
                Placeholder = "Path to a migrondi.json",
                Value = state.ConfigPath,
                OnChange = fun value -> state.ConfigPath <- value
              )
                .Build()
            ),
          actionButton(palette, "Browse...", fun () -> onEvent BrowseConfig),
          actionButton(palette, "Import", fun () -> onEvent Import)
        )
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

let virtualForm
  (
    palette: Palette,
    state: NewProjectState,
    onDriverSelect: Action<string>,
    onEvent: NewProjectEvent -> unit
  ) : Blob =
  Container(
    Key = "new-project-virtual",
    FlexDirection = FlexDirection.Column,
    Gap = 8,
    Padding = 12,
    BackgroundColor = palette.Panel,
    BorderRadius = 8,
    BorderWidth = edgesFromFloat 1.0,
    BorderColor = palette.Border
  )
    .Children(
      Text(
        Content = "Create a virtual project",
        FontSize = 14,
        Color = palette.Text
      ),
      fieldRow(
        palette,
        "Project name",
        TextField(
          Placeholder = "My project",
          Value = state.Name,
          OnChange = fun value -> state.Name <- value
        )
          .Build()
      ),
      fieldRow(
        palette,
        "Description (optional)",
        TextField(
          Placeholder = "What this project tracks",
          Value = state.Description,
          OnChange = fun value -> state.Description <- value
        )
          .Build()
      ),
      fieldRow(
        palette,
        "Connection string",
        TextField(
          Placeholder = "Data Source=./migrondi.db",
          Value = state.Connection,
          OnChange = fun value -> state.Connection <- value
        )
          .Build()
      ),
      fieldRow(
        palette,
        "Table name",
        TextField(
          Value = state.TableName,
          OnChange = fun value -> state.TableName <- value
        )
          .Build()
      ),
      fieldRow(
        palette,
        "Driver",
        driverPicker("new-project-driver", state, onDriverSelect)
      ),
      Container(FlexDirection = FlexDirection.Row, Gap = 8)
        .Children(
          Text(
            Content = state.Notice,
            FontSize = 12,
            Color = palette.TextMuted,
            FlexGrow = 1.0
          ),
          actionButton(palette, "Create", fun () -> onEvent CreateVirtual)
        )
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
  }

  // Created once at cell birth: the mounted driver picker input carries the
  // same delegate instance on every Build.
  let onDriverSelect = Action<string>(fun id -> state.Driver <- id)

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
          | CreateVirtual -> createVirtual operations
          | Import -> importLocal operations

        Container(
          Key = "new-project",
          FlexDirection = FlexDirection.Column,
          Gap = 12,
          Padding = 12,
          Width = percent 100.0,
          Height = percent 100.0,
          BackgroundColor = palette.Canvas
        )
          .Children(
            headerBar(palette, onEvent),
            importBar(palette, state, onEvent),
            scrollColumn(
              palette,
              "new-project-scroll",
              [| virtualForm(palette, state, onDriverSelect, onEvent) |]
            )
          )
  }
