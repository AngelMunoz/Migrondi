---
name: goo-widgets-fsharp
description: Drive the Goo.Widgets control library from F#. Covers value widgets (data structs with Build) versus mounted widgets (Cell of WidgetInput), the controlled-input contract (Value/OnChange/OnCommit, OnClick, OnSelect/OnSelectionChange, Open/OnOpenChange, ExpandedIds), the delegate-caching rule for input snapshots, nullable-token fluent setters, styling precedence and Create-Part factories, and virtualized lists. Use whenever F# code mounts or configures ListView, DataGrid, TreeView, Slider, TextField, ModalDialog, TabBar, MediaTransport, ProgressBar, or any other Goo.Widgets control.
compatibility: Requires the .NET 10 SDK and projects referencing Goo.Widgets and FunGoo.Widgets.
---

# Goo.Widgets from F#

Read `goo-fsharp` first: it holds the Cell/`Build`/Blob model, the owner rule, mounting,
and the state ladder. This skill covers the control library on top of that model. A
complete worked application lives in `samples/RaznorGoo` in this repository.

## Two widget kinds

A **value widget** is a data struct with `Build() -> Blob`. It holds no state. F# does not
apply the G# implicit conversion to `Blob`, so call `.Build()` when composing children:

```fsharp
Container().Children(
    ProgressBar(Value = 0.6).Build(),
    EmptyState(Title = "No songs").Build())
```

A **mounted widget** is a stateful `Cell<'TInput>` class. Mount it with the two-generic
form and a stable key:

```fsharp
Cell.Mount<MediaTransportInput, MediaTransport>(
    "media-transport",
    MediaTransportInput(
        Playing = false, Position = 12.0, Duration = 180.0, Volume = 0.5,
        OnPlayPause = onPlayPause, OnNext = onNext, OnPrevious = onPrevious))
```

The widget keeps its internals (handles, drag state, type-ahead) in the Cell instance.
App state and I/O stay outside, in your store and services.

## The controlled-input contract

The host owns the state. The widget renders from the input and emits requests through
nullable callbacks. After a callback fires, the owner rule rebuilds your Cell; your next
`Build` supplies the updated input, and the widget reseeds from it.

| Concern | Fields |
| --- | --- |
| Primary value | `Value` + `OnChange`, plus `OnCommit` for the final value |
| Activation | `OnClick` for one target, `On<Part>Click` for a part |
| Single selection | `SelectedId` + `OnSelect` |
| Multi selection | `SelectedIds` + `OnSelectionChange` |
| Open state | `Open` + `OnOpenChange` (+ `OnDismiss` only for a reason or item) |
| Expansion | `ExpandedIds` + `OnExpandedChange` |
| Query | `Query` + `OnQueryChange` |
| Field metadata | `Label`, `Description`, `Error` (never `Issue`) |
| Gating | `Disabled` blocks interaction and Tab; `ReadOnly` blocks edits, keeps selection and copy |

Rules:

- Update your copy of the value, then return from the handler. Never mutate the input's
  collections in place; build a new array or set.
- An empty selection set clears the selection. Activation is not selection.
- Uncontrolled mode: omit both the state field and the callback, and the widget keeps its
  own. Pick the mode once per widget; do not infer it from a missing callback.
- A widget never mutates caller collections. Treat every callback as a request.

## The delegate-caching rule

The sharpest rule in this library. A mounted input is compared whole with
`EqualityComparer<'TInput>.Default`. A closure created fresh inside a parent `Build`
changes the snapshot every rebuild, so the widget rebuilds whenever the parent rebuilds —
even when nothing changed.

Create the delegates once, in Cell fields outside `Build`:

```fsharp
type BottomBar(env: Env) =
    inherit Cell()
    // Created once: every input snapshot carries the same delegate
    // instances, so an unchanged input skips the rebuild.
    let onPlayPause () = env.Playback.PlayPause()
    let onSeek seconds = env.PostUI.Post(fun () -> env.Store.Position <- seconds)

    override _.Build() : Blob =
        Cell.Mount<MediaTransportInput, MediaTransport>(
            "media-transport",
            MediaTransportInput(Playing = ..., OnPlayPause = onPlayPause, ...))
```

Plain Blob callbacks are different: a fresh `Button(OnClick = fun () -> ...)` authored by
your own Cell is fine. Callbacks never gate node reuse, and the owner rule rebuilds your
Cell anyway.

## Configuring from F#

- Plain fields set through constructor named properties:
  `ListRow(Title = song.Name, Selected = isSelected)`.
- Nullable token fields (`Color`, `FontSize`, `Gap`, `Padding`, `Width`, `Opacity`,
  `TransitionMs`, ...) use the generated optional setters, which take `'T voption`, copy
  the struct, and are `static member inline` — the chain allocates nothing:

```fsharp
ListRow(Title = song.Name, Selected = isSelected, MinHeight = Nullable (lengthFromFloat 36.0))
    .backgroundColor(Color.Rgb(24, 31, 43))
    .selectedBackgroundColor(Color.Rgb(40, 52, 74))
    .Build()
```

- Nullable value arguments pass by name: `Padding = Nullable 20.`.
- Setters that collide with F# keywords use double-backtick names: `` widget.``open``(ValueSome true) ``.
- Part-style and factory fields are set the same way, when the widget accepts them.

## Styling

- Precedence is fixed: theme defaults, then explicit nullable tokens, then the part
  `Style` — applied in that order through `BasedOn`. Assign `BasedOn` before overrides.
- Part styles are nullable fields named `RootStyle` and `<Part>Style`:
  `TrackStyle`, `FillStyle`, `ThumbStyle`.
- Part factories are nullable `Func` fields named `Create<Part>`. They receive the owning
  descriptor first and a prepared default part last. Change visuals only. Preserve
  identity, row height, input routing, disabled state, and semantics.
- Pseudo-state styles sit on any Blob: `Hover`, `Active`, `Focus`, `DisabledStyle`.
  Animate with `TransitionMs`.
- Themes attach at the root (`Theme.Attach(window)`); a subtree overrides with
  `ThemeScope`. Shared palettes: `Syntax`, `Diff`, `SeriesPalette`.

## Virtualized lists

ListView, DataGrid, and TreeView realize rows through a virtual source. Feed them the
FunGoo way:

- Supply an array or an `IReadOnlyList<'T>` view. Never materialize a `seq` per build.
- One stable, unique string key per item. Index keys break on reorder.
- Fixed item height. The interop contract asks for "a retained builder delegate":
  create the key and builder delegates once, at cell birth — in the factory
  function or class constructor — and let every `Build` reuse them. A fresh `Func`
  wrapper per build is allocation for nothing, and a module-level value would be
  an ambient dependency: replacing it in a test means rewriting every call site.

```fsharp
// Created once per cell, outside Build, derived from the factory's
// parameters — so a test controls them through the factory, not by
// patching globals:
let create (songs: Song array, onSelect: Song -> unit) : Cell =
    let itemKey = Func<Song, string>(fun s -> s.Id)
    let buildRow = Func<Song, Blob>(fun s -> songRow (s, onSelect))

    { new Cell() with
        override _.Build() : Blob =
            // delegates already cached:
            // VirtualLists.Rows(songs, 36.0, itemKey, buildRow)
            ... }
```

- In a class-form cell, the constructor and its `let` bindings are the same
  home. `static let` is the one refinement for a stateless builder on a type
  instantiated many times; it stays private to the class, never module-level.

- `FunGoo.Virtual.ofIndexed` wraps count plus indexer in a list view without copying.

## Hosts and overlays

- `ModalDialogHost`: mount once, key it, never dispose it inside `Build`. Pass the
  `ModalDialog` value as its input; the host owns presentation.
- Popover, Tooltip, Menu, and Drawer present through the window's overlay. No manual
  z-order, no manual positioning.
- `FindBar` and `SearchList` are value structs whose `Build` mounts an internal state
  cell; treat them as value widgets and call `.Build()`.

## Widget map

| Job | Widgets |
| --- | --- |
| Actions | ActionButton, IconButton, ToggleButton |
| Inputs | TextField, TextArea, NumberInput, Slider, Checkbox, RadioButton, RadioGroup, ToggleSwitch, TokenInput, Stepper, Calendar, DatePicker, TimePicker, ColorPicker, ComboBox, RatingControl, FindBar, SearchList, TriggerMenu, ToggleGroup |
| Lists and data | ListView, DataGrid, TreeView, ListRow, Chip, AttachmentChip, Badge, PropertyGrid, TimeAxis |
| Feedback | ProgressBar, ProgressSummary, Banner, DismissibleContextBar, EmptyState, Skeleton, Spinner, Toast + ToastHost, TeachingTip |
| Layout | Card, Grid, Drawer, Disclosure, MasterDetail, SplitPane, AppBar, WindowChrome, SectionHeader, StatusBar, ModalDialog, HoverCard, Popover, SettingsLayout |
| Navigation | TabBar, Breadcrumb, Menu, MenuButton, CommandPalette, NavigationRail |
| Media and charts | AsyncImage, Avatar, MediaCard, MediaTransport, DonutChart, StackedBar, GraphCanvas, GraphNodeCard |
| Content and theme | CodeBlock, Kbd, MarkdownView (separate package), ThemeScope, MaterialIcons |

## Checklist for review

List each of these in the diff:

- `.Build()` missing on a value widget composed as a child.
- A delegate created inside `Build` for a mounted widget input.
- Host state mutated by the widget's own code instead of updated through a callback.
- An `OnChange` handler that mutates the input's collection in place.
- A `seq` materialized per build for a virtual list; keys unstable or index-based.
- A `Create<Part>` factory that changes identity, row height, routing, or disabled state.
- `BasedOn` assigned after the overrides it should base.
- A widget holding app state or doing I/O.

Each item: fix it, or state why it stays.

## Verify

In the target project:

1. `dotnet build` — the mount and setter shapes above must compile as written.
2. Call the parent `Build()` headless and assert the mounted input's fields on the
   returned tree.
3. Rebuild the parent twice with unchanged store data and measure allocated bytes around
   the second `Build()`: a cached-delegate input adds near zero; fresh closures show up
   here.
