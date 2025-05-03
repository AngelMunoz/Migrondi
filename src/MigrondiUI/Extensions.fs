[<AutoOpen>]
module Extensions

open Avalonia.Controls
open Avalonia.Controls.Primitives

open NXUI.Extensions


type ListBox with
  member inline this.SelectionMode(mode: SelectionMode) =
    this.SelectionMode <- mode
    this

type SelectingItemsControl with

  member inline this.OnSelectionChanged<'T>(f: 'T seq * 'T seq -> unit) =
    this.OnSelectionChangedHandler(fun _ args ->
      f(args.AddedItems |> Seq.cast<'T>, args.RemovedItems |> Seq.cast<'T>))
