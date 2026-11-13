module Migrondi.UI.Views.Shell

open System

open Goo
open Goo.Widgets
open Goo.Widgets.Feedback
open Goo.Widgets.Navigation
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Navs
open Navs.Goo

open MigrondiUI
open Migrondi.UI.Types
open Migrondi.UI.Views.Common
open Migrondi.UI.Views.Landing
open Migrondi.UI.Views.ProjectDetails
open Migrondi.UI.Views.NewProject

/// The root cell the composition root drives: `Router` hands the route
/// table to the outlet the shell mounts, `Start` kicks the first
/// navigation after the marshaller attaches the window, and `Refresh`
/// repaints the current page and the shell chrome after the appearance
/// changes. The abstract members keep the cell an object expression.
[<AbstractClass>]
type ShellCell() =
  inherit Cell()

  abstract Router: IRouter<Blob> with get

  /// The outlet the shell mounts; the composition root sets its `Window`
  /// after the window exists, so navigation change notices from background
  /// threads can post to the UI thread.
  abstract Outlet: RouterOutlet with get

  /// Switches the app palette and the widget theme together.
  abstract ToggleTheme: unit -> unit

  /// Rebuilds the current page and the shell chrome; the theme wiring calls
  /// it after the appearance changes.
  abstract Refresh: unit -> unit

  /// Lands on the projects page and kicks its load. Call after the
  /// marshaller attaches the window, so the load's Post is not dropped.
  abstract Start: unit -> unit

// The shell keeps the old app's chrome: a side menu with the app brand on
// top, the two destinations below it, and the footer under the content.
// Pages navigate through the env's router; the shell replaces `currentPage`
// on every navigation, so `Refresh` can repaint the page the outlet shows.
let create(env: IAppEnv, navigator: GooNavigator, feedback: Cell) : ShellCell =
  let mutable currentPage: Cell voption = ValueNone

  // The side menu's selected entry; entering a project from the list keeps
  // "Projects" selected, as the old side menu did.
  let mutable menuSelection = "projects"

  let notFound(projectId: Guid) : Blob =
    let palette = Palette.forTheme env.Theme.Store.Dark

    EmptyState(
      Title = "Project not found",
      Description = $"No project carries the id {projectId}",
      MinHeight = Nullable(lengthFromFloat 160.0)
    )
      .Build()

  let routes: RouteDefinition<Blob> list = [
    Route.define<Blob>(
      Routes.LandingRouteName,
      "/",
      fun (_: RouteContext) (_: INavigable) ->
        let cell = Landing.create env
        currentPage <- ValueSome(cell :> Cell)
        cell.Start()
        Cell.Mount((fun () -> cell :> Cell), "page-landing")
    )

    Route.define<Blob>(
      "new-project",
      "/projects/new",
      fun (_: RouteContext) (_: INavigable) ->
        let cell = NewProject.create env
        currentPage <- ValueSome cell
        Cell.Mount((fun () -> cell), "page-new-project")
    )

    Route.define<Blob>(
      "local-project-details",
      "/projects/local/:projectId<guid>",
      fun (context: RouteContext) (_: INavigable) -> asyncEx {
        match context.GetParam<Guid>("projectId") with
        | ValueSome projectId ->
          let! found = env.Projects.Get projectId

          match found with
          | Some(Local _ as project) ->
            let cell = ProjectDetails.create(env, project)

            currentPage <- ValueSome(cell :> Cell)
            cell.Start()

            return
              Cell.Mount((fun () -> cell :> Cell), $"page-local-{projectId}")
          | _ -> return notFound(projectId)
        | ValueNone -> return notFound(Guid.Empty)
      }
    )

    Route.define<Blob>(
      "virtual-project-details",
      "/projects/virtual/:projectId<guid>",
      fun (context: RouteContext) (_: INavigable) -> asyncEx {
        match context.GetParam<Guid>("projectId") with
        | ValueSome projectId ->
          let! found = env.Projects.Get projectId

          match found with
          | Some(Virtual _ as project) ->
            let cell = ProjectDetails.create(env, project)

            currentPage <- ValueSome(cell :> Cell)
            cell.Start()

            return
              Cell.Mount((fun () -> cell :> Cell), $"page-virtual-{projectId}")
          | _ -> return notFound(projectId)
        | ValueNone -> return notFound(Guid.Empty)
      }
    )
  ]

  let router = GooRouter(routes) :> IRouter<Blob>

  // Created once, beside the cell it builds: the feedback surface mounts
  // under one stable key for the whole app lifetime.
  let feedbackFactory = fun () -> feedback

  // The outlet renders the router's snapshot. The initial URI starts the
  // first navigation once; `Start` re-runs it only when it never ran.
  let outlet = new RouterOutlet(initialUri = "/")
  outlet.Router <- Some router

  let outletFactory = fun () -> outlet :> Cell

  // The router travels through the env before any page exists to navigate.
  navigator.Attach(router)

  // Cached once at birth: the mounted rail input carries the same delegate
  // and items every build, so an unchanged input skips the rail's rebuild.
  let railItems = [|
    NavigationItem(Id = "projects", Label = "Projects")
    NavigationItem(Id = "new-project", Label = "New Project")
  |]

  let railSelect(id: string) =
    match id with
    | "projects" ->
      menuSelection <- id
      Navigation.go(router, "/")
    | "new-project" ->
      menuSelection <- id
      Navigation.go(router, "/projects/new")
    | _ -> ()

  {
    new ShellCell() with
      override _.Router = router

      override _.Outlet = outlet

      override this.Build() : Blob =
        let dark = env.Theme.Store.Dark
        let palette = Palette.forTheme dark
        let themeLabel = if dark then "Theme: Dark" else "Theme: Light"

        let rail =
          Cell.Mount<NavigationRailInput, NavigationRail>(
            "side-nav",
            NavigationRailInput(
              Items = railItems,
              SelectedId = menuSelection,
              Expanded = true,
              OnSelect = railSelect,
              BackgroundColor = palette.Panel,
              SelectedBackgroundColor = palette.RowSelected,
              HoverBackgroundColor = palette.Row,
              TextColor = palette.Text,
              SelectedTextColor = palette.Text
            )
          )

        // The old app's side menu: brand on top, destinations below,
        // footer with the theme switch under the content.
        let sidebar =
          Container(
            Key = "shell-sidebar",
            FlexDirection = FlexDirection.Column,
            Width = 220,
            MinHeight = 0,
            BackgroundColor = palette.Panel
          )
            .Children(
              Container(
                Key = "shell-brand",
                FlexDirection = FlexDirection.Column,
                Gap = 2,
                Padding = 12,
                BorderWidth = topEdge 1.0,
                BorderColor = palette.Border
              )
                .Children(
                  Text(
                    Key = "shell-title",
                    Content = "Migrondi UI",
                    FontSize = 16,
                    Color = palette.Text
                  ),
                  Text(
                    Key = "shell-caption",
                    Content = "Migrations Management Tool",
                    FontSize = 11,
                    Color = palette.TextMuted
                  )
                ),
              Container(
                Key = "shell-menu",
                FlexGrow = 1.0,
                FlexShrink = 1.0,
                MinHeight = 0
              )
                .Children(rail),
              Container(
                Key = "shell-sidebar-footer",
                FlexDirection = FlexDirection.Column,
                Gap = 8,
                Padding = 12
              )
                .Children(
                  actionButton(
                    palette,
                    themeLabel,
                    fun () -> this.ToggleTheme()
                  ),
                  Text(
                    Key = "shell-footer",
                    Content = "Powered by Migrondi",
                    FontSize = 10,
                    Color = palette.TextMuted
                  )
                )
            )

        Container(
          Key = "shell",
          FlexDirection = FlexDirection.Row,
          Width = percent 100.0,
          Height = percent 100.0,
          BackgroundColor = palette.Canvas
        )
          .Children(
            sidebar,
            Container(
              Key = "shell-content",
              FlexDirection = FlexDirection.Column,
              FlexGrow = 1.0,
              FlexShrink = 1.0,
              MinWidth = 0,
              MinHeight = 0
            )
              .Children(
                Cell.Mount(outletFactory, "shell-outlet"),
                Cell.Mount(feedbackFactory, "feedback")
              )
          )

      override this.ToggleTheme() =
        env.Theme.Toggle()
        this.Refresh()

      override this.Refresh() =
        currentPage |> ValueOption.iter(fun cell -> cell.Rebuild())
        this.Rebuild()

      override this.Start() = outlet.Start()
  }
