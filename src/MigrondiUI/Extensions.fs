[<AutoOpen>]
module Extensions

open System
open System.Text
open System.Runtime.CompilerServices

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Templates
open Avalonia.Controls.Primitives
open Avalonia.Data
open Avalonia.Markup.Xaml.Styling
open Avalonia.Layout
open Avalonia.Styling
open AvaloniaEdit
open AvaloniaEdit.Document
open AvaloniaEdit.Highlighting

open NXUI.Extensions

open FsToolkit.ErrorHandling

open Navs
open Migrondi.Core

type AsyncOptionBuilder with

  member _.Source(value: 'T | null) : Async<'T option> =
    match value with
    | null -> Async.singleton None
    | value -> Async.singleton(Some value)

  member _.Source(value: string | null) : Async<string option> =
    match value with
    | null -> Async.singleton None
    | value -> Async.singleton(Some value)

  member _.Source(value: Async<'T | null>) : Async<'T option> =
    value
    |> Async.map (function
      | null -> None
      | value -> Some value)

  member _.Source(value: Async<string | null>) : Async<string option> =
    value
    |> Async.map (function
      | null -> None
      | value -> Some value)

type Styles with
  member inline this.Load(source: string) =
    StyleInclude(baseUri = null, Source = Uri(source)) |> this.Add

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

type ItemsRepeater with
  member inline this.ItemsSource(source: IBinding) =
    let descriptor =
      ItemsRepeater.ItemsSourceProperty
        .Bind()
        .WithMode(BindingMode.OneWay)
        .WithPriority(BindingPriority.LocalValue)

    this[descriptor] <- source
    this

  member inline this.ItemsSource(source: Control seq) =
    this.ItemsSource <- source
    this

  member inline this.ItemTemplate(template: IDataTemplate) =
    this.ItemTemplate <- template
    this

  member inline this.Layout(layout: AttachedLayout) =
    this.Layout <- layout
    this

  member inline this.HorizontalStack(?spacing: float) =
    let layout = StackLayout(Orientation = Orientation.Horizontal)
    spacing |> Option.iter(fun s -> layout.Spacing <- s)
    this.Layout <- layout
    this

  member inline this.VerticalStack(?spacing: float) =
    let layout = StackLayout(Orientation = Orientation.Vertical)
    spacing |> Option.iter(fun s -> layout.Spacing <- s)
    this.Layout <- layout
    this

type Grid with
  member inline this.RowDefinitions(defs: string) =
    this.RowDefinitions <- RowDefinitions.Parse(defs)
    this

  member inline this.ColumnDefinitions(defs: string) =
    this.ColumnDefinitions <- ColumnDefinitions.Parse(defs)
    this


type TextEditorExtensions =
  [<Extension>]
  static member inline ShowLineNumbers<'Type when 'Type :> TextEditor>
    (editor: 'Type, showLineNumbers: bool)
    : 'Type =
    editor.ShowLineNumbers <- showLineNumbers
    editor

  [<Extension>]
  static member inline Options<'Type when 'Type :> TextEditor>
    (editor: 'Type, options: TextEditorOptions)
    : 'Type =
    editor.Options <- options
    editor

  [<Extension>]
  static member inline Encoding<'Type when 'Type :> TextEditor>
    (editor: 'Type, encoding: Encoding)
    : 'Type =
    editor.Encoding <- encoding
    editor

  [<Extension>]
  static member inline Document<'Type when 'Type :> TextEditor>
    (editor: 'Type, document: TextDocument)
    : 'Type =
    editor.Document <- document
    editor

  [<Extension>]
  static member inline SyntaxHighlighting<'Type when 'Type :> TextEditor>
    (editor: 'Type, syntaxHighlighting: IHighlightingDefinition)
    : 'Type =
    editor.SyntaxHighlighting <- syntaxHighlighting
    editor

  [<Extension>]
  static member inline IsReadOnly<'Type when 'Type :> TextEditor>
    (editor: 'Type, isReadOnly: bool)
    : 'Type =
    editor.IsReadOnly <- isReadOnly
    editor

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

[<AutoOpen>]
module SukiUIExtensions =
  open Avalonia.Collections
  open SukiUI
  open SukiUI.Enums
  open SukiUI.Controls
  open SukiUI.Theme
  open Avalonia.Media


  type SukiWindow with
    member inline this.BackgroundStyle(style: SukiBackgroundStyle) =
      this.SetCurrentValue(SukiWindow.BackgroundStyleProperty, style)
      this


    member inline this.MenuItems([<ParamArray>] menuItems: MenuItem array) =
      this.SetCurrentValue(
        SukiWindow.MenuItemsProperty,
        AvaloniaList(menuItems)
      )

      this

    member inline this.MenuItems(menuItems: IBinding) =
      let descriptor =
        SukiWindow.MenuItemsProperty.Bind().WithMode(BindingMode.OneWay)

      this[descriptor] <- menuItems
      this

    member inline this.LogoContent(logo: Control) =
      this.SetCurrentValue(SukiWindow.LogoContentProperty, logo)
      this

    member inline this.RightWindowTitleBarControls
      ([<ParamArray>] controls: Control array)
      =
      this.SetCurrentValue(
        SukiWindow.RightWindowTitleBarControlsProperty,
        Controls(controls)
      )

      this

    member inline this.Hosts([<ParamArray>] controls: Control array) =
      this.SetCurrentValue(SukiWindow.HostsProperty, Controls(controls))

      this

  type SukiSideMenu with

    member inline this.MenuItems
      ([<ParamArray>] menuItems: SukiSideMenuItem array)
      =
      this.SetCurrentValue(SukiSideMenu.ItemsSourceProperty, menuItems)

      this

    member inline this.IsSearchEnabled(isSearchEnabled: bool) =
      this.SetCurrentValue(
        SukiSideMenu.IsSearchEnabledProperty,
        isSearchEnabled
      )

      this

    member inline this.HeaderContent(header: Control) =
      this.SetCurrentValue(SukiSideMenu.HeaderContentProperty, header)
      this

    member inline this.FooterContent(footer: Control) =
      this.SetCurrentValue(SukiSideMenu.FooterContentProperty, footer)
      this


  type SukiSideMenuItem with
    member inline this.Header(header: string) =
      this.SetCurrentValue(SukiSideMenuItem.HeaderProperty, header)
      this

    member inline this.Icon(icon: Control) =
      this.SetCurrentValue(SukiSideMenuItem.IconProperty, icon)
      this

    member inline this.IsContentMovable(isContentMovable: bool) =
      this.SetCurrentValue(
        SukiSideMenuItem.IsContentMovableProperty,
        isContentMovable
      )

      this

    member inline this.IsTopMenuExpanded(isTopMenuExpanded: bool) =
      this.SetCurrentValue(
        SukiSideMenuItem.IsTopMenuExpandedProperty,
        isTopMenuExpanded
      )

      this

    member inline this.PageContent(pageContent: Control) =
      this.SetCurrentValue(SukiSideMenuItem.PageContentProperty, pageContent)
      this

  type Button with

    member inline this.Icon(icon: StreamGeometry) =
      this.SetCurrentValue(ButtonExtensions.IconProperty, PathIcon(Data = icon))
      this

    member inline this.ShowProgress(showProgress: bool) =
      this.SetCurrentValue(ButtonExtensions.ShowProgressProperty, showProgress)
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

type MigrationStatus with

  member this.Migration =
    match this with
    | Applied m -> m
    | Pending m -> m
