module Migrondi.UI.Types

open System

open Goo
open Goo.Widgets
open Navs
open Migrondi.UI.Logic
open MigrondiUI.Services

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

/// The UI-thread seam. Background work returns through `Post`; no service
/// ever hands out a Window or a Cell.
type IPostUI =
  abstract Post: (unit -> unit) -> unit

/// Lists, registers, imports, exports, and deletes projects.
type IProvideProjects =
  abstract Projects: IProjectCollection

/// Builds one migration-operations set per project.
type IProvideMigrations =
  abstract Migrations: IMigrationOperationsFactory

type IProvidePostUI =
  abstract PostUI: IPostUI

/// The appearance the app palette and the widget theme agree on.
type ThemeStore = { mutable Dark: bool }

/// Reads the appearance and switches it. Pages read `Store` inside Build
/// (state ladder tier 4, read-only); the shell toggles and rebuilds.
type IThemeController =
  abstract Store: ThemeStore
  abstract Toggle: unit -> unit

type IProvideTheme =
  abstract Theme: IThemeController

/// Surfaces transient notices and confirmation questions. Implementations
/// marshal through `PostUI`, so callers may invoke from any thread.
type IFeedback =
  abstract Toast: message: string -> unit

  abstract Confirm:
    message: string * confirmLabel: string * onConfirm: (unit -> unit) -> unit

type IProvideFeedback =
  abstract Feedback: IFeedback

/// Builds OS file and folder pickers. The implementation opens Goo's
/// native dialogs, so callers invoke it from UI-thread callbacks only.
type IProvideFilePicker =
  abstract FilePicker: IFilePicker

/// The router travels through the env, so pages navigate with
/// `Navigate(url)` instead of shell closures.
type IProvideNavigator =
  abstract Navigator: IRouter<Blob>

/// Holds the router until the shell builds it from the route table. The
/// attach-late slot breaks the env/router construction cycle, like the
/// window slot in the marshaller; pages exist only after the attach.
type GooNavigator() =
  let mutable router: IRouter<Blob> voption = ValueNone

  member _.Attach(current: IRouter<Blob>) = router <- ValueSome current

  interface IProvideNavigator with
    member _.Navigator =
      match router with
      | ValueSome current -> current
      | ValueNone -> failwith "The navigator attaches before any page navigates"

/// The URLs and route names the pages navigate by. The shell authors the
/// route table from these, so pages and routes share one spelling.
[<RequireQualifiedAccess>]
module Routes =
  let LandingRouteName = "landing"

  let localProjectUrl(projectId: Guid) = $"/projects/local/{projectId}"

  let virtualProjectUrl(projectId: Guid) = $"/projects/virtual/{projectId}"

/// Starts a navigation and logs failures; the outlet renders the result.
/// `Navigate` returns a cold task: an unstarted call (for example
/// `Navigate url |> ignore`) goes nowhere.
module Navigation =

  let go(router: IRouter<Blob>, url: string) =
    asyncEx {
      match! router.Navigate url with
      | Ok _ -> ()
      | Error error -> eprintfn "navigation failed for %s: %A" url error
    }
    |> Async.Start

/// The production bundle, for the composition root only: one object that
/// satisfies every capability constraint the cells name.
type IAppEnv =
  inherit IProvideProjects
  inherit IProvideMigrations
  inherit IProvidePostUI
  inherit IProvideTheme
  inherit IProvideFeedback
  inherit IProvideFilePicker
  inherit IProvideNavigator

/// The window slot the Goo lifecycle forces: `Root` is init-only, so the
/// window is built last while background threads must marshal first. Attach
/// once after the window exists; every `Post` before that is dropped.
type UiMarshaller() =
  let mutable window: Window voption = ValueNone

  let post(action: unit -> unit) =
    window
    |> ValueOption.iter(fun window ->
      window.TryPost(fun () -> action()) |> ignore)

  member _.Attach(w: Window) = window <- ValueSome w

  member _.Post(action: unit -> unit) = post action

  interface IPostUI with
    member _.Post action = post action

/// Applies the appearance to the window's widget theme. The window slot
/// lives here, like the marshaller's, because the window is built after the
/// cells that want to switch themes.
type ThemeController(store: ThemeStore) =
  let mutable window: Window voption = ValueNone

  let apply(dark: bool) =
    window
    |> ValueOption.iter(fun window ->
      let mode = if dark then ThemeMode.Dark else ThemeMode.Light

      Theme.SetMode(window, mode))

  let toggle() =
    store.Dark <- not store.Dark
    apply(store.Dark)

  /// Widgets start following the OS appearance; the app palette takes the OS
  /// snapshot. An OS appearance change afterwards re-syncs both: the OS
  /// always wins over a manual toggle when the OS setting itself changes.
  member _.Attach(w: Window) =
    window <- ValueSome w
    Theme.Attach(w)

    store.Dark <- w.Preferences.Theme <> SystemTheme.Light

    w.add_PreferencesChanged(fun prefs ->
      match prefs.Theme with
      | SystemTheme.Light -> store.Dark <- false
      | SystemTheme.Dark -> store.Dark <- true
      | _ -> ()

      apply(store.Dark))

  member _.Toggle() = toggle()

  interface IThemeController with
    member _.Store = store

    member _.Toggle() = toggle()
