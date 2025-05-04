[<AutoOpen>]
module Extensions

open Avalonia.Controls
open Avalonia.Controls.Primitives

open NXUI.Extensions
open Navs


type ListBox with
  member inline this.SelectionMode(mode: SelectionMode) =
    this.SelectionMode <- mode
    this

  member inline this.SingleSelection() = this.SelectionMode SelectionMode.Single

  member inline this.MultipleSelection() =
    this.SelectionMode SelectionMode.Multiple

  member inline this.ToggleSelection() = this.SelectionMode SelectionMode.Toggle

  member inline this.AlwaysSelectedSelection() =
    this.SelectionMode SelectionMode.AlwaysSelected

type SelectingItemsControl with

  member inline this.OnSelectionChanged<'T>
    (onSelectionChanged: ('T seq * 'T seq) * SelectingItemsControl -> unit)
    =
    this.OnSelectionChangedHandler(fun _ args ->
      onSelectionChanged(
        (args.AddedItems |> Seq.cast<'T>, args.RemovedItems |> Seq.cast<'T>),
        this
      ))

type Grid with
  member inline this.RowDefinitions(defs: string) =
    this.RowDefinitions <- RowDefinitions.Parse(defs)
    this

  member inline this.ColumnDefinitions(defs: string) =
    this.ColumnDefinitions <- ColumnDefinitions.Parse(defs)
    this


type NavigationError<'View> with
  member this.StringError() =
    match this with
    | SameRouteNavigation -> "Navigated to the same route"
    | NavigationCancelled -> "Navigation cancelled"
    | RouteNotFound(url) -> $"Route not found: {url}"
    | NavigationFailed(message) -> $"Navigation failed: {message}"
    | CantDeactivate(deactivatedRoute) ->
      $"Can't deactivate route: {deactivatedRoute}"
    | CantActivate(activatedRoute) -> $"Can't activate route: {activatedRoute}"
    | GuardRedirect(redirectTo) -> $"Guard redirect to: {redirectTo}"
