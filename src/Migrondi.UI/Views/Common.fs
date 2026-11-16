module Migrondi.UI.Views.Common

open System

open Goo
open Goo.Widgets
open Goo.Widgets.Actions
open Goo.Widgets.Feedback
open Goo.Widgets.Inputs
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style
open FunGoo.Widgets

/// The app palette for one appearance: surfaces, text, the semantic tones
/// every page shares, and the SQL token colors the highlighter maps scopes
/// onto. Pages resolve it from the theme store inside Build, so a theme
/// switch repaints on the next rebuild.
type Palette = {
  Canvas: Color
  Panel: Color
  Row: Color
  RowSelected: Color
  AccentSoft: Color
  Border: Color
  Text: Color
  TextMuted: Color
  Accent: Color
  AccentText: Color
  Danger: Color
  Success: Color
  Warning: Color
  SqlKeyword: Color
  SqlString: Color
  SqlComment: Color
  SqlNumber: Color
  SqlOperator: Color
  SqlFunction: Color
}

module Palette =

  // Dark is the default: near-black canvas, one raised surface per region,
  // a single cool accent for primary actions and selection, and semantic
  // tones muted enough to sit on that canvas without vibrating.
  let dark: Palette = {
    Canvas = Color.Rgb(11, 14, 20)
    Panel = Color.Rgb(17, 22, 30)
    Row = Color.Rgb(23, 29, 39)
    RowSelected = Color.Rgb(31, 41, 56)
    AccentSoft = Color.Rgb(23, 38, 61)
    Border = Color.Rgb(33, 40, 52)
    Text = Color.Rgb(230, 236, 245)
    TextMuted = Color.Rgb(152, 163, 180)
    Accent = Color.Rgb(110, 168, 254)
    AccentText = Color.Rgb(9, 15, 25)
    Danger = Color.Rgb(227, 106, 106)
    Success = Color.Rgb(83, 192, 139)
    Warning = Color.Rgb(224, 164, 88)
    SqlKeyword = Color.Rgb(122, 172, 255)
    SqlString = Color.Rgb(136, 214, 165)
    SqlComment = Color.Rgb(118, 130, 148)
    SqlNumber = Color.Rgb(232, 189, 122)
    SqlOperator = Color.Rgb(196, 206, 222)
    SqlFunction = Color.Rgb(126, 208, 210)
  }

  let light: Palette = {
    Canvas = Color.Rgb(247, 248, 250)
    Panel = Color.Rgb(255, 255, 255)
    Row = Color.Rgb(241, 244, 248)
    RowSelected = Color.Rgb(228, 236, 247)
    AccentSoft = Color.Rgb(224, 235, 252)
    Border = Color.Rgb(224, 229, 237)
    Text = Color.Rgb(16, 21, 29)
    TextMuted = Color.Rgb(95, 107, 125)
    Accent = Color.Rgb(31, 111, 235)
    AccentText = Color.Rgb(255, 255, 255)
    Danger = Color.Rgb(192, 52, 43)
    Success = Color.Rgb(23, 128, 61)
    Warning = Color.Rgb(154, 103, 0)
    SqlKeyword = Color.Rgb(0, 76, 168)
    SqlString = Color.Rgb(18, 116, 58)
    SqlComment = Color.Rgb(116, 124, 140)
    SqlNumber = Color.Rgb(164, 106, 8)
    SqlOperator = Color.Rgb(66, 76, 98)
    SqlFunction = Color.Rgb(12, 118, 138)
  }

  let forTheme(useDark: bool) : Palette = if useDark then dark else light

// Button hierarchy. Exactly one primary action per screen; everything else
// is secondary or a quiet text action, so the next step is obvious without
// reading every label.
let primaryButton
  (palette: Palette, label: string, disabled: bool, onClick: unit -> unit)
  : Blob =
  ActionButton(
    Content = label,
    OnClick = (fun _ -> onClick()),
    Disabled = disabled
  )
    .backgroundColor(palette.Accent)
    .textColor(palette.AccentText)
    .hoverBackgroundColor(palette.Accent)
    .disabledBackgroundColor(palette.Row)
    .disabledTextColor(palette.TextMuted)
    .fontWeight(600)
    .Build()

let inline actionButton
  (palette: Palette, label: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Blob =
  ActionButton(Content = label, OnClick = (fun _ -> onClick()))
    .backgroundColor(palette.Panel)
    .textColor(palette.Text)
    .hoverBackgroundColor(palette.Row)
    .borderColor(palette.Border)
    .borderWidth(1.0)
    .Build()

// The gated variant: `disabled` is required (a fourth positional
// argument), because optional arguments on `inline` helpers break the
// existing three-argument call sites.
let inline actionButtonDisabled
  (
    palette: Palette,
    label: string,
    disabled: bool,
    [<InlineIfLambda>] onClick: unit -> unit
  ) : Blob =
  ActionButton(
    Content = label,
    OnClick = (fun _ -> onClick()),
    Disabled = disabled
  )
    .backgroundColor(palette.Panel)
    .textColor(palette.Text)
    .hoverBackgroundColor(palette.Row)
    .disabledBackgroundColor(palette.Panel)
    .disabledTextColor(palette.TextMuted)
    .borderColor(palette.Border)
    .borderWidth(1.0)
    .Build()

/// The destructive action: quiet outline, danger text, so it reads as
/// dangerous without competing with the primary action.
let inline dangerButton
  (
    palette: Palette,
    label: string,
    disabled: bool,
    [<InlineIfLambda>] onClick: unit -> unit
  ) : Blob =
  ActionButton(
    Content = label,
    OnClick = (fun _ -> onClick()),
    Disabled = disabled
  )
    .backgroundColor(palette.Panel)
    .textColor(palette.Danger)
    .hoverBackgroundColor(palette.Row)
    .disabledBackgroundColor(palette.Panel)
    .disabledTextColor(palette.TextMuted)
    .borderColor(palette.Border)
    .borderWidth(1.0)
    .Build()

/// A secondary action with a fixed minimum width: toolbar buttons keep
/// their label on one line instead of wrapping when the row is tight.
let inline sizedButton
  (
    palette: Palette,
    label: string,
    minWidth: float,
    disabled: bool,
    [<InlineIfLambda>] onClick: unit -> unit
  ) : Blob =
  ActionButton(
    Content = label,
    OnClick = (fun _ -> onClick()),
    Disabled = disabled
  )
    .backgroundColor(palette.Panel)
    .textColor(palette.Text)
    .hoverBackgroundColor(palette.Row)
    .disabledBackgroundColor(palette.Panel)
    .disabledTextColor(palette.TextMuted)
    .borderColor(palette.Border)
    .borderWidth(1.0)
    .minWidth(minWidth)
    .Build()

/// A quiet inline action: no surface of its own, muted until hovered.
let inline quietButton
  (palette: Palette, label: string, [<InlineIfLambda>] onClick: unit -> unit)
  : Blob =
  ActionButton(Content = label, OnClick = (fun _ -> onClick()))
    .backgroundColor(palette.Canvas)
    .textColor(palette.TextMuted)
    .hoverBackgroundColor(palette.Row)
    .borderWidth(0.0)
    .Build()

/// Shortens a string to `max` characters with a middle ellipsis, keeping
/// both ends: migration names carry the timestamp in the tail, and a list
/// row must never paint past its column.
let truncateMiddle (max: int) (text: string) : string =
  if String.IsNullOrEmpty text || text.Length <= max then
    text
  else
    let head = max * 2 / 3
    let tail = max - head - 1
    $"{text.Substring(0, head)}…{text.Substring(text.Length - tail)}"

/// A small tone chip: a label on a tinted surface. Used for project kinds,
/// migration status, and drivers so the eye can sort rows at a glance.
type Tone =
  | Neutral
  | Info
  | Good
  | Warn
  | Bad

let chipTone(palette: Palette, tone: Tone) : Color * Color =
  match tone with
  | Neutral -> palette.Row, palette.Text
  | Info -> palette.AccentSoft, palette.Accent
  | Good -> palette.Row, palette.Success
  | Warn -> palette.Row, palette.Warning
  | Bad -> palette.Row, palette.Danger

let inline chip(palette: Palette, tone: Tone, label: string) : Blob =
  let background, foreground = chipTone(palette, tone)

  Badge(
    Content = label,
    BackgroundColor = Nullable background,
    TextColor = Nullable foreground
  )
    .Build()

/// The page header every screen shares: title and subtitle on the left,
/// actions on the right, one divider under it.
let pageHeader
  (palette: Palette, title: string, subtitle: string, actions: Blob array)
  : Blob =
  Container(FlexDirection = FlexDirection.Column, Gap = 16).Children [|
    Container(
      FlexDirection = FlexDirection.Row,
      AlignItems = AlignItems.Center,
      Gap = 12
    )
      .Children
      [|
        Container(FlexDirection = FlexDirection.Column, Gap = 4, FlexGrow = 1.0)
          .Children(
            Text(
              Content = title,
              FontSize = 24,
              Color = palette.Text,
              FontWeight = 600
            ),
            Text(Content = subtitle, FontSize = 12, Color = palette.TextMuted)
          )
        :> Blob
        yield! actions
      |]
    :> Blob
    Container(
      Height = 1.0,
      Width = percent 100.0,
      BackgroundColor = palette.Border
    )
    :> Blob
  |]

/// A labelled control cluster: small uppercase label above the control, so
/// forms read as a grid of intent instead of a pile of boxes.
let fieldLabel(palette: Palette, label: string) : Blob =
  Text(
    Content = label,
    FontSize = 11,
    Color = palette.TextMuted,
    FontWeight = 600
  )

// A single-line field the page mounts: the abstract member keeps the cell
// an object expression while the page pushes external changes (picker
// results, cleared forms) through `SetText`.
[<AbstractClass>]
type TextFieldCell() =
  inherit Cell()

  /// Pushes an external value into the draft; no rebuild when unchanged.
  abstract SetText: string -> unit

// A field owns its draft (state ladder tier 1): keystrokes rebuild this
// cell only, never the page that mounts it. `onCommit` fires for callers
// that should react to a finished entry (search, filters) instead of every
// keystroke.
// The field cell paints to its container: a fixed default width is what
// clipped the settings form and stretched the toolbar field.
let textFieldWithCommit
  (
    placeholder: string,
    initial: string,
    onChange: string -> unit,
    onCommit: string -> unit
  ) : TextFieldCell =
  let mutable text = initial

  {
    new TextFieldCell() with
      override this.SetText(next: string) =
        if text <> next then
          text <- next
          this.Rebuild()

      override _.Build() : Blob =
        TextField(
          Placeholder = placeholder,
          Value = text,
          Width = Nullable(percent 100.0),
          OnChange =
            (fun value ->
              text <- value
              onChange value),
          OnCommit = (fun value -> onCommit value)
        )
          .Build()
  }

let textField
  (placeholder: string, initial: string, onChange: string -> unit)
  : TextFieldCell =
  textFieldWithCommit(placeholder, initial, onChange, ignore)

/// Mounts one field under a key with the page's current value. A page
/// creates one slot per field at cell birth and passes the slot to
/// `mountField` on every build.
type FieldMount = string * string -> Blob

// Creates a mount slot for one field. Goo calls the mount factory only
// when no retained cell can serve the mount, so the slot returns a fresh
// cell, seeded from the page's current value, on that call alone; while
// Goo retains the cell, the slot only pushes external value changes into
// it through `SetText`. A disposed slot target receives the same push:
// `Rebuild` on a disposed cell submits nowhere, so the push stays inert
// until the factory replaces the slot target on the next mount.
let mountedField(make: unit -> TextFieldCell) : FieldMount =
  let mutable live: TextFieldCell voption = ValueNone

  fun (key, current) ->
    live |> ValueOption.iter(fun field -> field.SetText current)

    Cell.Mount(
      (fun () ->
        let fresh = make()
        fresh.SetText current
        live <- ValueSome fresh
        fresh :> Cell),
      key
    )

// Mounts a field slot under a stable key: picker results and cleared
// forms repaint, keystrokes never leave the field.
let mountField(key: string, mount: FieldMount, current: string) : Blob =
  mount(key, current)

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
