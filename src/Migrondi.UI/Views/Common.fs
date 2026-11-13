module Migrondi.UI.Views.Common

open Goo
open Goo.Widgets.Actions
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Widgets

/// The app palette for one appearance: surfaces, text, and the SQL token
/// colors the highlighter maps scopes onto. Pages resolve it from the theme
/// store inside Build, so a theme switch repaints on the next rebuild.
type Palette = {
  Canvas: Color
  Panel: Color
  Row: Color
  RowSelected: Color
  Border: Color
  Text: Color
  TextMuted: Color
  Accent: Color
  SqlKeyword: Color
  SqlString: Color
  SqlComment: Color
  SqlNumber: Color
  SqlOperator: Color
  SqlFunction: Color
}

module Palette =

  let dark: Palette = {
    Canvas = Color.Rgb(20, 27, 39)
    Panel = Color.Rgb(16, 21, 31)
    Row = Color.Rgb(24, 31, 43)
    RowSelected = Color.Rgb(40, 52, 74)
    Border = Color.Rgb(35, 44, 62)
    Text = Color.Rgb(230, 235, 245)
    TextMuted = Color.Rgb(150, 160, 180)
    Accent = Color.Rgb(140, 190, 255)
    SqlKeyword = Color.Rgb(140, 190, 255)
    SqlString = Color.Rgb(150, 232, 180)
    SqlComment = Color.Rgb(150, 160, 180)
    SqlNumber = Color.Rgb(240, 200, 140)
    SqlOperator = Color.Rgb(200, 210, 230)
    SqlFunction = Color.Rgb(150, 232, 220)
  }

  let light: Palette = {
    Canvas = Color.Rgb(244, 246, 250)
    Panel = Color.Rgb(255, 255, 255)
    Row = Color.Rgb(238, 242, 248)
    RowSelected = Color.Rgb(222, 231, 246)
    Border = Color.Rgb(212, 219, 230)
    Text = Color.Rgb(26, 32, 44)
    TextMuted = Color.Rgb(104, 114, 134)
    Accent = Color.Rgb(30, 100, 190)
    SqlKeyword = Color.Rgb(0, 76, 168)
    SqlString = Color.Rgb(18, 116, 58)
    SqlComment = Color.Rgb(116, 124, 140)
    SqlNumber = Color.Rgb(164, 106, 8)
    SqlOperator = Color.Rgb(66, 76, 98)
    SqlFunction = Color.Rgb(12, 118, 138)
  }

  let forTheme(useDark: bool) : Palette = if useDark then dark else light

let inline actionButton
  (palette: Palette, label: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Blob =
  ActionButton(Content = label, OnClick = fun _ -> onClick())
    .backgroundColor(palette.Row)
    .textColor(palette.Text)
    .hoverBackgroundColor(palette.RowSelected)
    .Build()

/// A vertical scroll area. Goo paints a scrollbar only when the axis owns
/// authored Track and Thumb parts, so every scrolling pane goes through
/// here.
let scrollColumn(palette: Palette, key: string, children: Blob array) : Blob =
  Container(
    Key = key,
    FlexDirection = FlexDirection.Column,
    FlexGrow = 1.0,
    FlexShrink = 1.0,
    MinHeight = 0,
    OverflowY = Overflow.Scroll,
    OverflowX = Overflow.Hidden,
    ScrollbarY =
      Scrollbar(
        Thickness = 10.0,
        Track = Container(BackgroundColor = palette.Row),
        Thumb = Container(BackgroundColor = palette.TextMuted)
      ),
    Gap = 2
  )
    .Children(children)
