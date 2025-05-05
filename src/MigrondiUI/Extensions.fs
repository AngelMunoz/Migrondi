[<AutoOpen>]
module Extensions

open System.Runtime.CompilerServices

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout

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

[<Extension>]
type LayoutableExtensions =
  [<Extension>]
  static member inline MarginTop<'T when 'T :> Layoutable>
    (this: 'T, margin: float)
    =
    this.Margin <-
      Thickness(this.Margin.Left, margin, this.Margin.Right, this.Margin.Bottom)

    this

  [<Extension>]
  static member inline MarginBottom<'T when 'T :> Layoutable>
    (this: 'T, margin: float)
    =
    this.Margin <-
      Thickness(this.Margin.Left, this.Margin.Top, this.Margin.Right, margin)

    this

  [<Extension>]
  static member inline MarginLeft<'T when 'T :> Layoutable>
    (this: 'T, margin: float)
    =
    this.Margin <-
      Thickness(margin, this.Margin.Top, this.Margin.Right, this.Margin.Bottom)

    this

  [<Extension>]
  static member inline MarginRight<'T when 'T :> Layoutable>
    (this: 'T, margin: float)
    =
    this.Margin <-
      Thickness(this.Margin.Left, this.Margin.Top, margin, this.Margin.Bottom)

    this

  [<Extension>]
  static member inline MarginX<'T when 'T :> Layoutable>
    (this: 'T, margin: float)
    =
    this.Margin <-
      Thickness(margin, this.Margin.Top, margin, this.Margin.Bottom)

    this

  [<Extension>]
  static member inline MarginY<'T when 'T :> Layoutable>
    (this: 'T, margin: float)
    =
    this.Margin <-
      Thickness(this.Margin.Left, margin, this.Margin.Right, margin)

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
