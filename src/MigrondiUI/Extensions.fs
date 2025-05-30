[<AutoOpen>]
module Extensions

open System
open System.Runtime.CompilerServices

open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Markup.Xaml.Styling
open Avalonia.Layout
open Avalonia.Styling

open AvaloniaEdit

open NXUI.Extensions
open Navs
open Avalonia.Data
open System.Text
open AvaloniaEdit.Document
open AvaloniaEdit.Highlighting
open Avalonia.Controls.Templates

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


[<Extension>]
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
