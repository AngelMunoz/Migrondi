module MigrondiUI.Components.Fields

open Avalonia.Controls
open NXUI.Extensions
open System

[<AutoOpen>]
module private Styles =
  open Avalonia.Styling

  type StackPanel with

    member this.HorizontalFieldStyles() =
      this.Styles.AddRange [
        Style()
          .Selector(fun s ->
            s.OfType<StackPanel>().Class("LabeledField_Horizontal"))
          .SetStackLayoutOrientation(Avalonia.Layout.Orientation.Horizontal)
          .SetStackLayoutSpacing(4)
        Style()
          .Selector(fun s -> s.OfType<TextBlock>().Class("LabeledField_Label"))
          .SetTextBlockFontWeight(Avalonia.Media.FontWeight.Bold)
          .SetTextBlockFontSize(14)
        Style()
          .Selector(fun s -> s.OfType<TextBlock>().Class("LabeledField_Text"))
          .SetTextBlockFontSize(12.)
      ]

      this

    member this.VerticalFieldStyles() =
      this.Styles.AddRange [
        Style()
          .Selector(fun s ->
            s.OfType<StackPanel>().Class("LabeledField_Vertical"))
          .SetStackLayoutOrientation(Avalonia.Layout.Orientation.Vertical)
          .SetStackLayoutSpacing(4)
        Style()
          .Selector(fun s -> s.OfType<TextBlock>().Class("LabeledField_Label"))
          .SetTextBlockFontWeight(Avalonia.Media.FontWeight.Bold)
          .SetTextBlockFontSize(14)
        Style()
          .Selector(fun s -> s.OfType<TextBlock>().Class("LabeledField_Text"))
          .SetTextBlockFontSize(12.)
      ]

      this

type LabeledField =

  static member Horizontal(label: string, content: string, ?spacing: int) =
    StackPanel()
      .Classes("LabeledField_Horizontal")
      .Children(
        TextBlock().Classes("LabeledField_Label").Text label,
        TextBlock().Classes("LabeledField_Text").Text content
      )
      .HorizontalFieldStyles()
      .Spacing(defaultArg spacing 4)

  static member Horizontal(label: string, content: Control, ?spacing: int) =
    StackPanel()
      .Classes("LabeledField_Horizontal")
      .Children(TextBlock().Classes("LabeledField_Label").Text label, content)
      .HorizontalFieldStyles()
      .Spacing(defaultArg spacing 4)

  static member Vertical(label: string, content: string, ?spacing: int) =
    StackPanel()
      .Classes("LabeledField_Vertical")
      .Children(
        TextBlock().Classes("LabeledField_Label").Text label,
        TextBlock().Classes("LabeledField_Text").Text content
      )
      .VerticalFieldStyles()
      .Spacing(defaultArg spacing 4)

  static member Vertical(label: string, content: Control, ?spacing: int) =
    StackPanel()
      .Classes("LabeledField_Vertical")
      .Children(TextBlock().Classes("LabeledField_Label").Text label, content)
      .VerticalFieldStyles()
      .Spacing(defaultArg spacing 4)
