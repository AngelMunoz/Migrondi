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
}

// One event channel for the page's actions; the cell dispatches it.
type LandingEvent =
  | Refresh
  | Browse
  | Register
  | Open of Project

// Chip colors that read on both appearances: saturated backgrounds with a
// near-white foreground.
let kindBadge(project: Project) : Blob =
  let content, background =
    match project with
    | Local _ -> "Local", Color.Rgb(43, 61, 89)
    | Virtual _ -> "Virtual", Color.Rgb(35, 74, 52)

  Badge(
    Content = content,
    BackgroundColor = Nullable background,
    TextColor = Nullable(Color.Rgb(235, 240, 248))
  )
    .Build()

// One row per project. Activation opens the project; it is not selection.
let inline projectRow
  (
    palette: Palette,
    project: Project,
    [<InlineIfLambda>] onEvent: LandingEvent -> unit
  ) : Blob =
  let caption =
    match project with
    | Local p -> p.migrondiConfigPath
    | Virtual p -> p.connection

  let description = project.Description |> Option.defaultValue caption

  ListRow(
    Title = project.Name,
    Caption = description,
    Trailing = kindBadge project,
    OnClick = (fun _ -> onEvent(Open project)),
    MinHeight = Nullable(lengthFromFloat 44.0),
    PaddingVertical = Nullable 8.0
  )
    .backgroundColor(palette.Row)
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
let headerBar
  (palette: Palette, state: LandingState, onEvent: LandingEvent -> unit)
  : Blob =
  Container(
    FlexDirection = FlexDirection.Row,
    Gap = 8,
    AlignItems = AlignItems.Center
  )
    .Children(
      Text(
        Key = "landing-title",
        Content = "Projects",
        FontSize = 20,
        Color = palette.Text,
        FlexGrow = 1.0
      ),
      Text(
        Key = "landing-notice",
        Content = state.Notice,
        FontSize = 12,
        Color = palette.TextMuted
      ),
      actionButton(palette, "Refresh", fun () -> onEvent Refresh)
    )

let registerBar
  (palette: Palette, state: LandingState, onEvent: LandingEvent -> unit)
  : Blob =
  Container(
    FlexDirection = FlexDirection.Row,
    Gap = 8,
    AlignItems = AlignItems.Center
  )
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
      actionButton(palette, "Browse...", fun () -> onEvent Browse),
      actionButton(palette, "Register local", fun () -> onEvent Register)
    )

let projectRows
  (palette: Palette, state: LandingState, onEvent: LandingEvent -> unit)
  : Blob array =
  if Array.isEmpty state.Projects then
    [|
      EmptyState(
        Title = "No projects",
        Description = "Register a migrondi.json below to see it here",
        MinHeight = Nullable(lengthFromFloat 160.0)
      )
        .Build()
    |]
  else
    state.Projects |> Array.map(fun p -> projectRow(palette, p, onEvent))

let page
  (palette: Palette, state: LandingState, onEvent: LandingEvent -> unit)
  : Blob =
  Container(
    Key = "landing",
    FlexDirection = FlexDirection.Column,
    Gap = 12,
    Padding = 12,
    Width = percent 100.0,
    Height = percent 100.0,
    BackgroundColor = palette.Canvas
  )
    .Children(
      headerBar(palette, state, onEvent),
      registerBar(palette, state, onEvent),
      scrollColumn(
        palette,
        "landing-projects",
        projectRows(palette, state, onEvent)
      )
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
  }

  {
    new LandingCell() with
      override this.Start() = reload(this, env, state)

      override this.Build() : Blob =
        let palette = Palette.forTheme env.Theme.Store.Dark

        let onEvent =
          function
          | Refresh -> reload(this, env, state)
          | Browse -> browse(this, env, state)
          | Register -> register(this, env, state)
          | Open project ->
            let url =
              match project with
              | Local p -> Routes.localProjectUrl p.id
              | Virtual p -> Routes.virtualProjectUrl p.id

            Navigation.go(env.Navigator, url)

        page(palette, state, onEvent)
  }
