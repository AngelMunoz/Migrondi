module Migrondi.UI.Views.Landing

open System
open System.IO

open Goo
open Goo.Widgets
open Goo.Widgets.Feedback
open Goo.Widgets.Inputs
open Goo.Widgets.Layout
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style
open FunGoo.Widgets

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open MigrondiUI
open Migrondi.UI.Types
open Migrondi.UI.Views.Common

// The landing state: one mutable field per concern, one immutable value per
// concern. The cell reads it inside Build; the transition functions below
// replace whole values.
type LandingState = {
  mutable Projects: Project array
  mutable ConfigPath: string
  mutable Notice: string
  mutable Query: string
  mutable RegisterOpen: bool
}

// One event channel for the page's actions; the cell dispatches it.
type LandingEvent =
  | Refresh
  | Browse
  | Register
  | ToggleRegister
  | NewProject
  | Search of string
  | Open of Project

// One row per project. Activation opens the project; it is not selection.
// The row carries the kind chip and a chevron, so scanning the list answers
// "what is this project and how do I get in" without reading the caption.
let inline projectRow
  (
    palette: Palette,
    project: Project,
    [<InlineIfLambda>] onEvent: LandingEvent -> unit
  ) : Blob =
  let caption, kind, tone =
    match project with
    | Local p -> p.migrondiConfigPath, "Local", Info
    | Virtual p -> p.connection, "Virtual", Neutral

  let trailing =
    Container(
      FlexDirection = FlexDirection.Row,
      AlignItems = AlignItems.Center,
      Gap = 12
    )
      .Children
      [|
        chip(palette, tone, kind)
        Text(Content = "›", FontSize = 16, Color = palette.TextMuted)
      |]

  ListRow(
    Title = project.Name,
    Caption = caption,
    Trailing = trailing,
    OnClick = (fun _ -> onEvent(Open project)),
    MinHeight = Nullable(lengthFromFloat 52.0),
    PaddingVertical = Nullable 10.0
  )
    .backgroundColor(palette.Panel)
    .hoverBackgroundColor(palette.Row)
    .textColor(palette.Text)
    .Build()

// State transitions: plain functions, callable without a window.
let loaded(state: LandingState, found: Project array) =
  state.Projects <- found

  state.Notice <-
    if Array.isEmpty found then
      "No projects registered yet"
    else
      $"{found.Length} projects"

let registered(state: LandingState, name: string, found: Project array) =
  state.ConfigPath <- ""
  state.RegisterOpen <- false
  state.Projects <- found
  state.Notice <- $"Registered {name}"

let failed(state: LandingState, context: string, ex: exn) =
  state.Notice <- $"{context} failed: {ex.Message}"

/// A landing page the shell starts: the abstract member keeps the cell an
/// object expression while outside code gets a callable entry point.
[<AbstractClass>]
type LandingCell() =
  inherit Cell()

  /// Kicks the initial load. Call after the marshaller attaches the window,
  /// so the load's Post is not dropped.
  abstract Start: unit -> unit

// Background operations: the cell arrives as a parameter, so these stay
// plain functions and the object expression stays non-recursive.

// Runs on the UI thread from the Build-authored callback: the picker needs
// it to start the native chooser. The await part continues on a worker.
let browse<'env when 'env :> IProvideFilePicker and 'env :> IProvidePostUI>
  (cell: LandingCell, env: 'env, state: LandingState)
  : unit =
  asyncEx {
    let! picked = env.FilePicker.PickProjectConfig()

    match picked with
    | Some path ->
      env.PostUI.Post(fun () ->
        state.ConfigPath <- path
        cell.Rebuild())
    | None -> ()
  }
  |> Async.StartImmediate

let reload<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideFeedback>
  (cell: LandingCell, env: 'env, state: LandingState)
  : unit =
  asyncEx {
    try
      let! found = env.Projects.List()
      let items = Array.ofList found

      env.PostUI.Post(fun () ->
        loaded(state, items)
        cell.Rebuild())
    with ex ->
      env.PostUI.Post(fun () ->
        failed(state, "Loading projects", ex)
        env.Feedback.Toast($"Loading projects failed: {ex.Message}")
        cell.Rebuild())
  }
  |> Async.Start

let register<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideFeedback>
  (cell: LandingCell, env: 'env, state: LandingState)
  : unit =
  asyncEx {
    let path = state.ConfigPath.Trim()

    if String.IsNullOrWhiteSpace path then
      state.Notice <- "Enter the path to a migrondi.json first"
    else
      try
        let! _ = env.Projects.RegisterLocal path
        let! found = env.Projects.List()
        let items = Array.ofList found

        let name =
          Path.GetFileName path |> Option.ofObj |> Option.defaultValue path

        env.PostUI.Post(fun () ->
          registered(state, name, items)
          cell.Rebuild())
      with ex ->
        env.PostUI.Post(fun () ->
          failed(state, "Registering", ex)
          env.Feedback.Toast($"Registering failed: {ex.Message}")
          cell.Rebuild())
  }
  |> Async.Start

// View pieces: plain functions over the palette and state, one event
// callback each.
let registerBar
  (
    palette: Palette,
    state: LandingState,
    configField: FieldMount,
    onEvent: LandingEvent -> unit
  ) : Blob =
  Container(
    FlexDirection = FlexDirection.Column,
    Gap = 8,
    Padding = 12,
    BackgroundColor = palette.Panel,
    BorderRadius = 8
  )
    .Children(
      Container(
        FlexDirection = FlexDirection.Row,
        Gap = 8,
        AlignItems = AlignItems.Center
      )
        .Children(
          Container(FlexGrow = 1.0, MinWidth = 0)
            .Children(
              mountField("landing-config", configField, state.ConfigPath)
            ),
          actionButton(palette, "Browse...", fun () -> onEvent Browse),
          primaryButton(palette, "Register", false, fun () -> onEvent Register)
        )
    )

let projectRows
  (palette: Palette, state: LandingState, onEvent: LandingEvent -> unit)
  : Blob array =
  let query = state.Query.Trim()

  let matching =
    if String.IsNullOrEmpty query then
      state.Projects
    else
      state.Projects
      |> Array.filter(fun project ->
        let text =
          match project with
          | Local p -> $"{p.name} {p.migrondiConfigPath}"
          | Virtual p -> $"{p.name} {p.connection}"

        text.Contains(query, StringComparison.OrdinalIgnoreCase))

  if Array.isEmpty state.Projects then
    [|
      EmptyState(
        Title = "No projects yet",
        Description =
          "Create a project, or register an existing migrondi.json on disk",
        MinHeight = Nullable(lengthFromFloat 180.0)
      )
        .Build()
    |]
  elif Array.isEmpty matching then
    [|
      EmptyState(
        Title = "No matches",
        Description = $"No project matches \"{query}\"",
        MinHeight = Nullable(lengthFromFloat 140.0)
      )
        .Build()
    |]
  else
    matching |> Array.map(fun p -> projectRow(palette, p, onEvent))

let page
  (
    palette: Palette,
    state: LandingState,
    configField: FieldMount,
    searchField: FieldMount,
    onEvent: LandingEvent -> unit
  ) : Blob =
  let localCount, virtualCount =
    state.Projects
    |> Array.fold
      (fun (locals, virtuals) project ->
        match project with
        | Local _ -> locals + 1, virtuals
        | Virtual _ -> locals, virtuals + 1)
      (0, 0)

  let subtitle =
    if Array.isEmpty state.Projects then
      "No projects registered yet"
    else
      $"{localCount} local · {virtualCount} virtual"

  let actions = [|
    actionButton(palette, "Refresh", fun () -> onEvent Refresh)
    actionButton(palette, "Register existing", fun () -> onEvent ToggleRegister)
    primaryButton(palette, "New project", false, fun () -> onEvent NewProject)
  |]

  // The header carries the actions; the search sits in its own row so the
  // list starts immediately below it.
  let searchRow =
    Container(Width = 320.0)
      .Children(mountField("landing-search", searchField, state.Query))

  Container(
    Key = "landing",
    FlexDirection = FlexDirection.Column,
    Gap = 16,
    Padding = 24,
    Width = percent 100.0,
    Height = percent 100.0,
    BackgroundColor = palette.Canvas
  )
    .Children(
      pageHeader(palette, "Projects", subtitle, actions),
      searchRow,
      scrollColumn(
        palette,
        "landing-projects",
        projectRows(palette, state, onEvent)
      ),
      if state.RegisterOpen then
        registerBar(palette, state, configField, onEvent)
      else
        Container(Height = 0.0)
    )

// The landing page owns the projects list (state ladder tier 2). Loads run
// on background threads and return through PostUI.
let create<'env
  when 'env :> IProvideProjects
  and 'env :> IProvidePostUI
  and 'env :> IProvideTheme
  and 'env :> IProvideFeedback
  and 'env :> IProvideFilePicker
  and 'env :> IProvideNavigator>
  (env: 'env)
  : LandingCell =
  let state = {
    Projects = Array.empty
    ConfigPath = ""
    Notice = "Loading projects..."
    Query = ""
    RegisterOpen = false
  }

  // Created once at cell birth: the mounted fields carry their own drafts,
  // so typing never rebuilds the project list. Search commits (Enter or
  // focus loss), so it rebuilds once per search instead of per keystroke.
  let configField =
    mountedField(fun () ->
      textField(
        "Path to a migrondi.json",
        "",
        fun value -> state.ConfigPath <- value
      ))

  // The commit callback needs the cell, but the field is created before the
  // object expression exists; the cell installs the refresh in Start.
  let mutable refreshAfterSearch = ignore

  let searchField =
    mountedField(fun () ->
      textFieldWithCommit(
        "Search projects (Enter)",
        "",
        ignore,
        fun value ->
          state.Query <- value
          refreshAfterSearch()
      ))

  {
    new LandingCell() with
      override this.Start() =
        refreshAfterSearch <- fun () -> this.Rebuild()
        reload(this, env, state)

      override this.Build() : Blob =
        let palette = Palette.forTheme env.Theme.Store.Dark

        let onEvent =
          function
          | Refresh -> reload(this, env, state)
          | Browse -> browse(this, env, state)
          | Register -> register(this, env, state)
          | ToggleRegister ->
            state.RegisterOpen <- not state.RegisterOpen
            this.Rebuild()
          | NewProject -> Navigation.go(env.Navigator, "/projects/new")
          | Search _ -> this.Rebuild()
          | Open project ->
            let url =
              match project with
              | Local p -> Routes.localProjectUrl p.id
              | Virtual p -> Routes.virtualProjectUrl p.id

            Navigation.go(env.Navigator, url)

        page(palette, state, configField, searchField, onEvent)
  }
