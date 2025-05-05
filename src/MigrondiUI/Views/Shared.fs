namespace MigrondiUI.Views

open Avalonia.Controls
open NXUI.Extensions


[<AutoOpen>]
module Shared =

  type LabeledField =

    static member inline Horizontal
      (label: string, content: string, ?spacing: int)
      =
      StackPanel()
        .Classes("LabeledField_Horizontal")
        .OrientationHorizontal()
        .Spacing(defaultArg spacing 4)
        .Children(
          TextBlock().Classes("LabeledField_Label").Text label,
          TextBlock().Classes("LabeledField_Text").Text content
        )

    static member inline Vertical
      (label: string, content: string, ?spacing: int)
      =
      StackPanel()
        .Classes("LabeledField_Vertical")
        .OrientationVertical()
        .Spacing(defaultArg spacing 4)
        .Children(
          TextBlock().Classes("LabeledField_Label").Text label,
          TextBlock().Classes("LabeledField_Text").Text content
        )
