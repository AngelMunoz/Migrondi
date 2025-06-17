namespace MigrondiUI.Components

open System
open System.Runtime.CompilerServices

open Avalonia.Controls
open Avalonia.Controls.Templates

open FSharp.Data.Adaptive
open Navs.Avalonia

[<Struct>]
type ToolbarOrientation =
  | Horizontal
  | Vertical

type ToolbarProps =
  | Spacing of float
  | Orientation of ToolbarOrientation

[<Struct>]
type private ToolbarArgs = {
  spacing: float voption
  orientation: ToolbarOrientation
}


module private ToolbarArgs =
  let toToolbarArgs(props: ToolbarProps seq) =
    props
    |> Seq.fold
      (fun (current: ToolbarArgs) next ->
        match next with
        | Orientation o ->
          let orientation =
            match o with
            | Horizontal -> Horizontal
            | Vertical -> Vertical

          {
            current with
                orientation = orientation
          }
        | Spacing s -> { current with spacing = ValueSome s })
      {
        spacing = ValueNone
        orientation = Horizontal
      }

type Toolbar =
  static member get([<ParamArray>] props: ToolbarProps array) =
    let args = ToolbarArgs.toToolbarArgs(props)

    let toolbar =
      ItemsRepeater()
        .ItemTemplate(FuncDataTemplate<Control>(fun props _ -> props))

    match args.orientation with
    | Horizontal ->
      toolbar.HorizontalStack(?spacing = (args.spacing |> ValueOption.toOption))
    | Vertical ->
      toolbar.VerticalStack(?spacing = (args.spacing |> ValueOption.toOption))

  [<Extension>]
  static member Children
    (toolbar: ItemsRepeater, [<ParamArray>] children: Control array)
    =
    toolbar.ItemsSource(children)

  [<Extension>]
  static member Children(toolbar: ItemsRepeater, children: Control array aval) =
    toolbar.ItemsSource(children |> AVal.toBinding)
