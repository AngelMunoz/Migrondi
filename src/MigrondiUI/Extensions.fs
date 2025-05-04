[<AutoOpen>]
module Extensions

open Avalonia.Controls
open Avalonia.Controls.Primitives

open NXUI.Extensions


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
