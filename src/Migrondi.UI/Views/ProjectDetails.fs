module Migrondi.UI.Views.ProjectDetails

open System
open System.Collections.Generic
open System.IO
open System.Threading.Tasks

open Goo
open Goo.Widgets
open Goo.Widgets.Content
open Goo.Widgets.Feedback
open Goo.Widgets.Inputs
open Goo.Widgets.Layout
open Goo.Widgets.Navigation
open TextMateSharp.Grammars
open TextMateSharp.Registry
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style
open FunGoo.Widgets

open FsToolkit.ErrorHandling
open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Migrondi.Core
open MigrondiUI
open MigrondiUI.Services
open Migrondi.UI.Types
open Migrondi.UI.Views.Common
open Migrondi.UI.Views.MigrationEditor
open Migrondi.UI.Views.ProjectSettings

// Precomputed SQL styles for one migration, plus the migration and the
// palette they were computed for, so a stale tokenization never lands on
// the current pane.
type MigrationSpans = {
  Owner: Migration
  Palette: Palette
  Up: IReadOnlyList<TextStyleSpan>
  Down: IReadOnlyList<TextStyleSpan>
}

// One migration section as the content pane shows it.
type CodeSection = {
  Key: string
  Label: string
  Content: string
  Spans: IReadOnlyList<TextStyleSpan> voption
}

// The details state: one mutable field per concern, one immutable value per
// concern. The cell reads it inside Build; the transition functions below
// replace whole values.
type DetailsState = {
  mutable Statuses: MigrationStatus array
  mutable Output: string
  mutable DryPreview: (bool * Migration array) voption
  mutable Busy: bool
  mutable NewName: string
  mutable Selected: Migration voption
  mutable Spans: MigrationSpans voption
  mutable Editing: bool
  mutable Section: string
  mutable NewOpen: bool
  mutable LimitSteps: bool
  mutable Step: float
}

// One event channel for the page's actions; the cell dispatches it.
type DetailsEvent =
  | Back
  | DryUp
  | DryDown
  | Apply
  | Rollback
  | Refresh
  | Create
  | Settings
  | Show of Migration
  | ToggleEdit
  | ToggleNew
  | SelectSection of string

// Status as a chip: the tone carries the meaning, the label confirms it.
let statusChip(palette: Palette, status: MigrationStatus) : Blob =
  match status with
  | Applied _ -> chip(palette, Good, "Applied")
  | Pending _ -> chip(palette, Warn, "Pending")

// One row per migration. The left edge carries a status dot so scanning the
// column answers "what is left" without reading each chip.
let statusRow
  (
    palette: Palette,
    status: MigrationStatus,
    selected: Migration voption,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  let migration =
    match status with
    | Applied m -> m
    | Pending m -> m

  let date =
    DateTimeOffset.FromUnixTimeMilliseconds(migration.timestamp).ToString("G")

  let caption =
    if migration.manualTransaction then
      $"{date} · manual"
    else
      date

  let dot =
    match status with
    | Applied _ -> palette.Success
    | Pending _ -> palette.Warning

  let trailing =
    Container(
      Width = 8.0,
      Height = 8.0,
      BorderRadius = 4.0,
      BackgroundColor = dot
    )

  ListRow(
    Title = truncateMiddle 30 migration.name,
    Caption = caption,
    Trailing = trailing,
    OnClick = (fun _ -> onEvent(Show migration)),
    Selected = (selected = ValueSome migration),
    SelectedBackgroundColor = Nullable palette.RowSelected,
    MinHeight = Nullable(lengthFromFloat 52.0),
    PaddingVertical = Nullable 10.0
  )
    .backgroundColor(palette.Panel)
    .hoverBackgroundColor(palette.Row)
    .textColor(palette.Text)
    .Build()

// State transitions: plain functions, callable without a window.
let listed(state: DetailsState, found: MigrationStatus array) =
  state.Statuses <- found
  state.Busy <- false
  state.DryPreview <- ValueNone

// The right pane shows the latest result: an operation that reports into the
// output pane releases the selected migration's code view, as the old app's
// pane switched to the dry-run result. Clicking a row selects it again.
let paneReleased(state: DetailsState) =
  state.Selected <- ValueNone
  state.Spans <- ValueNone

let shown
  (state: DetailsState, up: bool, preview: Migration array, summary: string)
  =
  paneReleased state
  state.DryPreview <- ValueSome(up, preview)
  state.Output <- summary
  state.Busy <- false

let applied
  (state: DetailsState, found: MigrationStatus array, summary: string)
  =
  paneReleased state
  state.Statuses <- found
  state.Output <- summary
  state.Busy <- false

let created(state: DetailsState, name: string, found: MigrationStatus array) =
  paneReleased state
  state.NewName <- ""
  state.Statuses <- found
  state.Output <- $"Created {name}"
  state.Busy <- false

// Selecting a migration drops the previous tokenization right away, so the
// pane paints plain content until this migration's spans come back.
let selected(state: DetailsState, migration: Migration) =
  state.Selected <- ValueSome migration
  state.Spans <- ValueNone

// Entering edit mode just flips the flag: the editor cell the page mounts
// copies the pending migration's content into its own draft fields.
let editStarted(state: DetailsState) = state.Editing <- true

let editEnded(state: DetailsState) = state.Editing <- false

// True when the selected migration still counts as pending, so the edit
// and delete actions apply to it.
let selectedIsPending(state: DetailsState) : bool =
  match state.Selected with
  | ValueSome migration ->
    state.Statuses
    |> Array.exists(function
      | Pending p -> p.name = migration.name
      | Applied _ -> false)
  | ValueNone -> false

// Spans land only while the selection is unchanged: a stale result for an
// older migration is discarded.
let highlighted(state: DetailsState, spans: MigrationSpans) =
  match state.Selected with
  | ValueSome current when current = spans.Owner ->
    state.Spans <- ValueSome spans
  | _ -> ()

let failed(state: DetailsState, context: string, ex: exn) =
  paneReleased state
  state.Output <- $"{context} failed ({ex.GetType().Name}): {ex.Message}"
  state.Busy <- false
  state.DryPreview <- ValueNone

// What an operation needs: the cell to rebuild, the marshaller to return
// through, the state to write, the migration operations to drive, the SQL
// grammar to tokenize with, the appearance to pick token colors for, and
// the feedback surface for toasts. One carrier so the operations below stay
// one-parameter functions.
type Operations = {
  Cell: Cell
  PostUI: IPostUI
  State: DetailsState
  Ops: IMigrationOperations
  Sql: IGrammar
  Theme: ThemeStore
  Feedback: IFeedback
}

// The step count the run and dry-run operations feed the engine: none when
// the limit toggle is off (all migrations), the number otherwise.
let stepAmount(state: DetailsState) : int option =
  if state.LimitSteps then Some(int state.Step) else None

// Background operations: the cell arrives as a parameter, so these stay
// plain functions and the object expression stays non-recursive.
let reload(operations: Operations) : unit =
  asyncEx {
    try
      let! ct = Async.CancellationToken
      let! found = operations.Ops.Core.MigrationsListAsync ct
      let items = Array.ofSeq found

      operations.PostUI.Post(fun () ->
        listed(operations.State, items)
        operations.Cell.Rebuild())
    with ex ->
      operations.PostUI.Post(fun () ->
        failed(operations.State, "Loading migrations", ex)
        operations.Cell.Rebuild())
  }
  |> Async.Start

let runDry(operations: Operations, up: bool) : unit =
  asyncEx {
    operations.State.Busy <- true
    let amount = stepAmount operations.State

    try
      let! ct = Async.CancellationToken

      let heading = if up then "Would apply" else "Would revert"

      let! preview =
        if up then
          operations.Ops.Core.DryRunUpAsync(
            ?amount = amount,
            cancellationToken = ct
          )
        else
          operations.Ops.Core.DryRunDownAsync(
            ?amount = amount,
            cancellationToken = ct
          )

      let items = preview |> Array.ofSeq

      let summary =
        if Array.isEmpty items then
          $"{heading}: nothing"
        else
          match amount with
          | Some limit ->
            $"{heading} {min limit items.Length} of {items.Length} migrations"
          | None -> $"{heading} {items.Length} migrations"

      operations.PostUI.Post(fun () ->
        shown(operations.State, up, items, summary)
        operations.Feedback.Toast summary
        operations.Cell.Rebuild())
    with ex ->
      let context = if up then "Dry run up" else "Dry run down"

      operations.PostUI.Post(fun () ->
        failed(operations.State, context, ex)
        operations.Feedback.Toast $"{context} failed: {ex.Message}"
        operations.Cell.Rebuild())
  }
  |> Async.Start

let run(operations: Operations, up: bool) : unit =
  asyncEx {
    operations.State.Busy <- true
    let amount = stepAmount operations.State

    try
      let! ct = Async.CancellationToken

      if up then
        do!
          operations.Ops.Core.RunUpAsync(
            ?amount = amount,
            cancellationToken = ct
          )
          :> Task
      else
        do!
          operations.Ops.Core.RunDownAsync(
            ?amount = amount,
            cancellationToken = ct
          )
          :> Task

      let! found = operations.Ops.Core.MigrationsListAsync ct
      let items = Array.ofSeq found

      let touched =
        items
        |> Array.choose(function
          | Applied m -> Some m.name
          | Pending _ -> None)
        |> String.concat ", "

      let summary =
        if String.IsNullOrEmpty touched then
          (if up then "Applied: nothing" else "Reverted: nothing")
        else
          (if up then $"Applied: {touched}" else $"Reverted: {touched}")

      operations.PostUI.Post(fun () ->
        applied(operations.State, items, summary)
        operations.Feedback.Toast summary
        operations.Cell.Rebuild())
    with ex ->
      let context = if up then "Apply" else "Rollback"

      operations.PostUI.Post(fun () ->
        failed(operations.State, context, ex)
        operations.Feedback.Toast $"{context} failed: {ex.Message}"
        operations.Cell.Rebuild())
  }
  |> Async.Start

// Destructive runs ask first; the confirm continuation starts the run on
// the UI thread.
let confirmRun(operations: Operations, up: bool) : unit =
  let target =
    match stepAmount operations.State with
    | Some count -> $"{count} migration(s)"
    | None -> "all migrations"

  let action = if up then "Apply" else "Rollback"

  operations.Feedback.Confirm(
    $"{action} {target}?",
    action,
    fun () -> run(operations, up)
  )

// Opens the local project's root folder in the OS file explorer.
let openFolder(operations: Operations, project: LocalProject) : unit =
  asyncEx {
    match
      project.migrondiConfigPath |> Path.GetDirectoryName |> Option.ofObj
    with
    | None -> operations.Feedback.Toast "Could not resolve the project folder"
    | Some root ->
      try
        do! Migrondi.UI.OsOperations.OpenDirectory root
      with ex ->
        operations.PostUI.Post(fun () ->
          operations.Feedback.Toast $"Open folder failed: {ex.Message}"
          operations.Cell.Rebuild())
  }
  |> Async.Start

let createMigration(operations: Operations) : unit =
  asyncEx {
    let name = operations.State.NewName.Trim()

    if String.IsNullOrWhiteSpace name then
      operations.State.Output <- "Enter a migration name first"
    else
      operations.State.Busy <- true

      try
        let! ct = Async.CancellationToken
        let! _ = operations.Ops.Core.RunNewAsync(name, cancellationToken = ct)
        let! found = operations.Ops.Core.MigrationsListAsync ct
        let items = Array.ofSeq found

        operations.PostUI.Post(fun () ->
          created(operations.State, name, items)
          operations.Feedback.Toast $"Created {name}"
          operations.Cell.Rebuild())
      with ex ->
        operations.PostUI.Post(fun () ->
          failed(operations.State, "New migration", ex)
          operations.Feedback.Toast $"New migration failed: {ex.Message}"
          operations.Cell.Rebuild())
  }
  |> Async.Start


// Maps one token's scopes to a palette color; the first matching prefix
// wins and anything unknown paints as plain text.
let colorForScope(palette: Palette, scopes: string seq) : Color =
  scopes
  |> Seq.tryPick(fun scope ->
    if scope.StartsWith "keyword.operator" then
      Some palette.SqlOperator
    elif scope.StartsWith "keyword" then
      Some palette.SqlKeyword
    elif scope.StartsWith "string" then
      Some palette.SqlString
    elif scope.StartsWith "comment" then
      Some palette.SqlComment
    elif
      scope.StartsWith "constant.numeric"
      || scope.StartsWith "constant.language"
    then
      Some palette.SqlNumber
    elif
      scope.StartsWith "entity.name.function"
      || scope.StartsWith "support.function"
      || scope.StartsWith "entity.name.type"
      || scope.StartsWith "support.type"
      || scope.StartsWith "storage.type"
    then
      Some palette.SqlFunction
    else
      None)
  |> Option.defaultValue palette.Text

// Runs on a background thread: tokenize the text line by line, carry the
// rule stack across lines, and map every token to a keyed style span over
// the whole content.
let highlightText
  (palette: Palette, grammar: IGrammar, text: string)
  : TextStyleSpan array =
  let spans = ResizeArray<TextStyleSpan>()

  if not(String.IsNullOrEmpty text) then
    // Keep line ends inside the line text so offsets stay absolute over
    // the exact string the CodeBlock displays.
    let lines = text.Split('\n')
    let mutable offset = 0

    // The first line tokenizes with a null rule stack, as the one-argument
    // TokenizeLine overload itself passes.
    let mutable stack = Unchecked.defaultof<IStateStack>

    for line in lines do
      let result =
        grammar.TokenizeLine(LineText line, stack, TimeSpan.FromSeconds 10.)

      stack <- result.RuleStack

      for token in result.Tokens do
        let length = token.EndIndex - token.StartIndex

        if length > 0 then
          let mutable from = offset + token.StartIndex
          // A grammar can look past the line end, so clamp to the line.
          let mutable stop = min (from + length) (offset + line.Length)

          if stop > from then
            // The document rejects ranges that split surrogate pairs.
            if
              from > 0
              && Char.IsHighSurrogate(text.[from - 1])
              && Char.IsLowSurrogate(text.[from])
            then
              from <- from - 1

            if
              stop < text.Length
              && Char.IsHighSurrogate(text.[stop - 1])
              && Char.IsLowSurrogate(text.[stop])
            then
              stop <- stop + 1

            let style = Style(Color = colorForScope(palette, token.Scopes))

            spans.Add(
              TextStyleSpan(
                string spans.Count,
                TextRange(from, stop - from),
                style
              )
            )

      offset <- offset + line.Length + 1

  spans.ToArray()

// The tokenization runs on a worker thread and returns through PostUI; the
// pane paints plain content until the spans arrive. The selection is
// already in place, so no state changes on the worker.
let tokenizeMigration(operations: Operations, migration: Migration) : unit =
  asyncEx {
    let palette = Palette.forTheme operations.Theme.Dark

    try
      let up = highlightText(palette, operations.Sql, migration.upContent)
      let down = highlightText(palette, operations.Sql, migration.downContent)

      operations.PostUI.Post(fun () ->
        highlighted(
          operations.State,
          {
            Owner = migration
            Palette = palette
            Up = up
            Down = down
          }
        )

        operations.Cell.Rebuild())
    with ex ->
      operations.PostUI.Post(fun () ->
        failed(operations.State, "Highlighting migration", ex)
        operations.Cell.Rebuild())
  }
  |> Async.Start

// Selection flips synchronously, then the tokenization starts.
let showMigration(operations: Operations, migration: Migration) : unit =
  selected(operations.State, migration)
  tokenizeMigration(operations, migration)
// The editor mount key: one stable key per migration, so each migration
// keeps its own draft while it is selected again.
let editorKey(migration: Migration) : string =
  $"details-editor-{migration.timestamp}-{migration.name}"

// The editor reports a saved draft: evict its cell, reload the list, drop
// edit mode, and re-select the migration so it tokenizes fresh.
let editorSaved
  (
    operations: Operations,
    editors: Dictionary<string, MigrationEditorCell>,
    updated: Migration
  ) : unit =
  asyncEx {
    editors.Remove(editorKey updated) |> ignore

    try
      let! ct = Async.CancellationToken
      let! found = operations.Ops.Core.MigrationsListAsync ct
      let items = Array.ofSeq found

      let fresh =
        items
        |> Array.tryPick(function
          | Pending p when
            p.name = updated.name || p.timestamp = updated.timestamp
            ->
            Some p
          | _ -> None)

      operations.PostUI.Post(fun () ->
        listed(operations.State, items)
        editEnded(operations.State)

        match fresh with
        | Some current -> selected(operations.State, current)
        | None -> operations.State.Selected <- ValueNone

        operations.Feedback.Toast $"Saved {updated.name}"
        operations.Cell.Rebuild())

      match fresh with
      | Some current -> tokenizeMigration(operations, current)
      | None -> ()
    with ex ->
      operations.PostUI.Post(fun () ->
        failed(operations.State, "Reloading migrations", ex)
        operations.Feedback.Toast $"Reload failed: {ex.Message}"
        operations.Cell.Rebuild())
  }
  |> Async.StartImmediate

// The editor reports a deletion: evict its cell, clear the selection,
// reload the list, and rebuild the page.
let editorDeleted
  (operations: Operations, editors: Dictionary<string, MigrationEditorCell>)
  : unit =
  asyncEx {
    match operations.State.Selected with
    | ValueNone -> ()
    | ValueSome current ->
      editors.Remove(editorKey current) |> ignore

      try
        let! ct = Async.CancellationToken
        let! found = operations.Ops.Core.MigrationsListAsync ct
        let items = Array.ofSeq found

        operations.PostUI.Post(fun () ->
          operations.State.Selected <- ValueNone
          operations.State.Spans <- ValueNone
          editEnded(operations.State)
          listed(operations.State, items)
          operations.Feedback.Toast $"Deleted {current.name}"
          operations.Cell.Rebuild())
      with ex ->
        operations.PostUI.Post(fun () ->
          failed(operations.State, "Reloading migrations", ex)
          operations.Feedback.Toast $"Reload failed: {ex.Message}"
          operations.Cell.Rebuild())
  }
  |> Async.StartImmediate

// The editor reports a cancelled edit: evict its draft and leave edit mode.
// Runs on the UI thread, straight from the editor's outcome channel.
let editorCancelled
  (operations: Operations, editors: Dictionary<string, MigrationEditorCell>)
  : unit =
  match operations.State.Selected with
  | ValueSome current -> editors.Remove(editorKey current) |> ignore
  | ValueNone -> ()

  editEnded(operations.State)
  operations.Cell.Rebuild()

// The outcome channel the mounted editors report through.
let onEditorOutcome
  (operations: Operations, editors: Dictionary<string, MigrationEditorCell>)
  : EditorOutcome -> unit =
  function
  | Saved migration -> editorSaved(operations, editors, migration)
  | Deleted -> editorDeleted(operations, editors)
  | Cancelled -> editorCancelled(operations, editors)

// Mounts the migration's editor cell: one retained instance per migration
// under its stable key, so drafts survive parent rebuilds and theme flips
// repaint through `SetDark`.
let mountEditor
  (
    operations: Operations,
    editors: Dictionary<string, MigrationEditorCell>,
    migration: Migration
  ) : Blob =
  let key = editorKey migration

  let editor =
    match editors.TryGetValue key with
    | true, existing -> existing
    | false, _ ->
      let args: EditorArgs = {
        PostUI = operations.PostUI
        Feedback = operations.Feedback
        Ops = operations.Ops
        Theme = operations.Theme
        Migration = migration
      }

      let fresh =
        MigrationEditor.create(args, onEditorOutcome(operations, editors))

      editors[key] <- fresh
      fresh

  editor.SetDark operations.Theme.Dark

  Cell.Mount(Func<Cell>(fun () -> editor :> Cell), key)


/// A details page the shell starts: the abstract member keeps the cell an
/// object expression while outside code gets a callable entry point.
[<AbstractClass>]
type DetailsCell() =
  inherit Cell()

  /// Creates the tracking table and kicks the first migration listing.
  abstract Start: unit -> unit

  /// Loads the migration list synchronously for a headless capture, which
  /// renders before the posted listing lands.
  abstract Snapshot: unit -> unit

// The page header: identity on the left, the run actions on the right.
// Project settings live on their own screen, reached from here.
let header
  (
    palette: Palette,
    state: DetailsState,
    project: Project,
    onStepChange: Action<float>,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  let pending =
    state.Statuses
    |> Array.sumBy(function
      | Pending _ -> 1
      | Applied _ -> 0)

  let applied =
    state.Statuses
    |> Array.sumBy(function
      | Applied _ -> 1
      | Pending _ -> 0)

  let title, path, kind, tone =
    match project with
    | Local p -> p.name, p.migrondiConfigPath, "Local", Info
    | Virtual p -> p.name, p.connection, "Virtual", Neutral

  let subtitle = $"{kind} · {pending} pending · {applied} applied"

  pageHeader(
    palette,
    title,
    subtitle,
    [|
      primaryButton(
        palette,
        $"Apply {pending}",
        state.Busy || pending = 0,
        fun () -> onEvent Apply
      )
      dangerButton(
        palette,
        $"Roll back {applied}",
        state.Busy || applied = 0,
        (fun () -> onEvent Rollback)
      )
    |]
  )

// The run bar: previews and the step bound on the left, project-level
// controls on the right. Keeping it out of the header is what lets the
// header stay one line at the default window size.
let runBar
  (
    palette: Palette,
    state: DetailsState,
    onStepChange: Action<float>,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  Container(
    Key = "details-run-bar",
    FlexDirection = FlexDirection.Row,
    Gap = 8,
    AlignItems = AlignItems.Center
  )
    .Children(
      sizedButton(
        palette,
        "Dry run up",
        108.0,
        state.Busy,
        fun () -> onEvent DryUp
      ),
      sizedButton(
        palette,
        "Dry run down",
        124.0,
        state.Busy,
        fun () -> onEvent DryDown
      ),
      ToggleSwitch(
        Value = state.LimitSteps,
        AccessibilityName = "Limit migrations",
        OnChange = fun limit -> state.LimitSteps <- limit
      )
        .Build(),
      Container(Width = 76.0)
        .Children(
          Cell.Mount<NumberInputInput, NumberInput>(
            "details-step",
            NumberInputInput(
              Value = state.Step,
              Minimum = Nullable 1.0,
              Step = Nullable 1.0,
              ShowSpinButtons = Nullable false,
              Disabled = not state.LimitSteps,
              Width = Nullable(lengthFromFloat 76.0),
              AccessibilityName = "Migration count",
              OnChange = onStepChange,
              OnCommit = onStepChange
            )
          )
        ),
      Text(
        Content = (if state.Busy then "Working…" else ""),
        FontSize = 12,
        Color = palette.TextMuted,
        FlexGrow = 1.0
      ),
      actionButton(palette, "Refresh", fun () -> onEvent Refresh),
      actionButton(palette, "Settings", fun () -> onEvent Settings)
    )

// The list header owns creating a migration: the input only exists while
// the user is adding one, so the list keeps the width.
let listHeader
  (
    palette: Palette,
    state: DetailsState,
    nameField: FieldMount,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  let title =
    Text(
      Content = $"Migrations · {state.Statuses.Length}",
      FontSize = 12,
      Color = palette.TextMuted,
      FontWeight = 600,
      FlexGrow = 1.0
    )

  let newRow =
    if state.NewOpen then
      Container(FlexDirection = FlexDirection.Column, Gap = 8)
        .Children(
          mountField("details-new-name", nameField, state.NewName),
          Container(FlexDirection = FlexDirection.Row, Gap = 8)
            .Children(
              Container(FlexGrow = 1.0)
                .Children(
                  primaryButton(
                    palette,
                    "Create",
                    state.Busy,
                    fun () -> onEvent Create
                  )
                ),
              actionButton(palette, "Cancel", fun () -> onEvent ToggleNew)
            )
        )
    else
      Container(Height = 0.0)

  Container(FlexDirection = FlexDirection.Column, Gap = 10)
    .Children(
      Container(
        FlexDirection = FlexDirection.Row,
        Gap = 8,
        AlignItems = AlignItems.Center
      )
        .Children(
          title,
          actionButtonDisabled(
            palette,
            "+ New",
            state.Busy,
            (fun () -> onEvent ToggleNew)
          )
        ),
      newRow
    )

let migrationRows
  (palette: Palette, state: DetailsState, onEvent: DetailsEvent -> unit)
  : Blob array =
  if Array.isEmpty state.Statuses then
    [|
      EmptyState(
        Title = "No migrations",
        Description = "Create one with the field above",
        MinHeight = Nullable(lengthFromFloat 140.0)
      )
        .Build()
    |]
  else
    state.Statuses
    |> Array.map(fun status ->
      statusRow(palette, status, state.Selected, onEvent))

// One dry-run section as the preview pane shows it: the direction's
// content, wrapped in the transaction markers the engine would emit unless
// the migration opts out with manualTransaction.
let dryContent(up: bool, migration: Migration) : string =
  let content = if up then migration.upContent else migration.downContent

  if migration.manualTransaction then
    content
  else
    $"-- ----------START TRANSACTION----------\n{content}\n-- ----------COMMIT TRANSACTION----------"

// One mounted CodeBlock per section. The spans reference stays put while
// the content is unchanged, so the widget never re-highlights; omitting it
// shows the plain content while a tokenization is still running.
let codePane(section: CodeSection) : Blob =
  let input =
    match section.Spans with
    | ValueSome spans ->
      CodeBlockInput(
        Code = section.Content,
        Language = section.Label,
        StyleSpans = spans,
        ShowLineNumbers = true,
        Width = Nullable(percent 100.0)
      )
    | ValueNone ->
      CodeBlockInput(
        Code = section.Content,
        Language = section.Label,
        ShowLineNumbers = true,
        Width = Nullable(percent 100.0)
      )

  Cell.Mount<CodeBlockInput, CodeBlock>(section.Key, input)

// The right pane: the dry-run result while there is one, otherwise nothing
// until a migration is selected. A stale "no operation" line is noise, so
// an idle pane explains what to do instead.
let outputPane(palette: Palette, state: DetailsState) : Blob =
  let preview: Blob array =
    match state.DryPreview with
    | ValueSome(up, migrations) ->
      migrations
      |> Array.map(fun migration ->
        codePane {
          Key = $"details-dry-{migration.timestamp}-{migration.name}"
          Label = (if up then "up · SQL" else "down · SQL")
          Content = dryContent(up, migration)
          Spans = ValueNone
        })
    | ValueNone -> [||]

  let content: Blob array =
    if String.IsNullOrWhiteSpace state.Output && Array.isEmpty preview then
      [|
        EmptyState(
          Title = "Nothing selected",
          Description =
            "Pick a migration to read its SQL, or preview a run to see what would happen",
          MinHeight = Nullable(lengthFromFloat 200.0)
        )
          .Build()
      |]
    else
      [|
        if String.IsNullOrWhiteSpace state.Output then
          Container(Height = 0.0)
        else
          Text(
            Key = "details-output-text",
            Content = state.Output,
            FontSize = 13,
            Color = palette.Text
          )
          :> Blob
        yield! preview
      |]

  Container(
    FlexDirection = FlexDirection.Column,
    FlexGrow = 1.0,
    FlexShrink = 1.0,
    MinWidth = 0,
    Gap = 12,
    Padding = 16
  )
    .Children(scrollColumn(palette, "details-output", content))

// The right pane: the selected migration as an Up/Down tab pair, one code
// block at a time instead of two stacked walls of SQL. Editing mounts the
// editor cell; a theme switch shows plain content until spans repaint.
let migrationPane
  (
    palette: Palette,
    state: DetailsState,
    mountEditor: Migration -> Blob,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  match state.Selected with
  | ValueNone -> outputPane(palette, state)
  | ValueSome migration ->
    let upSpans, downSpans =
      match state.Spans with
      | ValueSome spans when spans.Owner = migration && spans.Palette = palette ->
        ValueSome spans.Up, ValueSome spans.Down
      | _ -> ValueNone, ValueNone

    let pending = selectedIsPending state
    let editing = pending && state.Editing
    let showUp = state.Section <> "down"

    let sections: Blob array =
      if editing then
        [| mountEditor migration |]
      else
        let tabs =
          Cell.Mount<TabBarInput, TabBar>(
            "details-sections",
            TabBarInput(
              Items = [|
                NavigationItem(Id = "up", Label = "Migrate up")
                NavigationItem(Id = "down", Label = "Migrate down")
              |],
              SelectedId = state.Section,
              OnSelect = fun id -> onEvent(SelectSection id)
            )
          )

        let headerRow =
          Container(
            FlexDirection = FlexDirection.Row,
            Gap = 10,
            AlignItems = AlignItems.Center
          )
            .Children
            [|
              Text(
                Content = truncateMiddle 44 migration.name,
                FontSize = 16,
                Color = palette.Text,
                FontWeight = 600,
                FlexGrow = 1.0
              )
              :> Blob
              statusChip(
                palette,
                (if pending then Pending migration else Applied migration)
              )
              if pending then
                actionButton(palette, "Edit", fun () -> onEvent ToggleEdit)
            |]

        [|
          headerRow
          tabs
          codePane {
            Key =
              if showUp then
                "details-content-up"
              else
                "details-content-down"
            Label = (if showUp then "up · SQL" else "down · SQL")
            Content =
              (if showUp then
                 migration.upContent
               else
                 migration.downContent)
            Spans = (if showUp then upSpans else downSpans)
          }
        |]

    Container(
      Key = "details-content",
      FlexGrow = 1.0,
      FlexShrink = 1.0,
      MinWidth = 0,
      Padding = 16
    )
      .Children(scrollColumn(palette, "details-content-scroll", sections))

let body
  (
    palette: Palette,
    state: DetailsState,
    nameField: FieldMount,
    mountEditor: Migration -> Blob,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  let list =
    Container(
      FlexDirection = FlexDirection.Column,
      Gap = 12,
      Width = 340.0,
      MinWidth = 280.0
    )
      .Children(
        listHeader(palette, state, nameField, onEvent),
        scrollColumn(
          palette,
          "details-migrations",
          migrationRows(palette, state, onEvent)
        )
      )

  Container(
    Key = "details-body",
    FlexDirection = FlexDirection.Row,
    Gap = 16,
    FlexGrow = 1.0,
    FlexShrink = 1.0,
    MinHeight = 0
  )
    .Children(list, migrationPane(palette, state, mountEditor, onEvent))

let page
  (
    palette: Palette,
    state: DetailsState,
    project: Project,
    nameField: FieldMount,
    onStepChange: Action<float>,
    mountEditor: Migration -> Blob,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  Container(
    Key = "project-details",
    FlexDirection = FlexDirection.Column,
    Gap = 20,
    Padding = 24,
    Width = percent 100.0,
    Height = percent 100.0,
    BackgroundColor = palette.Canvas
  )
    .Children(
      header(palette, state, project, onStepChange, onEvent),
      runBar(palette, state, onStepChange, onEvent),
      body(palette, state, nameField, mountEditor, onEvent)
    )

// The details page owns its slice of app state (state ladder tier 2).
let create<'env
  when 'env :> IProvideProjects
  and 'env :> IProvideMigrations
  and 'env :> IProvidePostUI
  and 'env :> IProvideTheme
  and 'env :> IProvideFeedback
  and 'env :> IProvideFilePicker
  and 'env :> IProvideNavigator>
  (env: 'env, project: Project)
  : DetailsCell =
  let state = {
    Statuses = Array.empty
    Output = ""
    DryPreview = ValueNone
    Busy = false
    NewName = ""
    Selected = ValueNone
    Spans = ValueNone
    Editing = false
    Section = "up"
    NewOpen = false
    LimitSteps = false
    Step = 1.0
  }

  let ops = env.Migrations.Create project

  // The SQL grammar lives in this closure, not in module state, so a test
  // builds the page with its own registry.
  let options = RegistryOptions(ThemeName.DarkPlus)

  let sql: IGrammar =
    Registry(options).LoadGrammar(options.GetScopeByExtension(".sql"))

  // Created once at cell birth: the mounted fields carry their own drafts,
  // so typing never repaints the page, and the NumberInput input carries
  // the same delegate instance on every Build.
  let onStepChange = Action<float>(fun value -> state.Step <- value)

  let nameField =
    mountedField(fun () ->
      textField("new migration name", "", fun value -> state.NewName <- value))

  // One retained editor cell per migration, so drafts survive parent
  // rebuilds; evicted when the migration is saved, deleted, or discarded.
  let editors = Dictionary<string, MigrationEditorCell>()

  let operations(this: Cell) : Operations = {
    Cell = this
    PostUI = env.PostUI
    State = state
    Ops = ops
    Sql = sql
    Theme = env.Theme.Store
    Feedback = env.Feedback
  }

  // Created once, beside the cell it mounts: the settings screen owns its
  // own state; this page only navigates there.
  let settingsUrl =
    match project with
    | Local p -> Routes.localSettingsUrl p.id
    | Virtual p -> Routes.virtualSettingsUrl p.id

  {
    new DetailsCell() with
      override this.Start() =
        // The tracking table exists before the first listing.
        ops.Core.Initialize()
        reload(operations this)

      override this.Snapshot() =
        ops.Core.Initialize()
        listed(state, Array.ofSeq(ops.Core.MigrationsList()))
        this.Rebuild()

      override this.Build() : Blob =
        let dark = env.Theme.Store.Dark
        let palette = Palette.forTheme dark

        let onEvent =
          function
          | Back -> Navigation.go(env.Navigator, "/")
          | DryUp -> runDry(operations this, true)
          | DryDown -> runDry(operations this, false)
          | Apply -> confirmRun(operations this, true)
          | Rollback -> confirmRun(operations this, false)
          | Refresh -> reload(operations this)
          | Create -> createMigration(operations this)
          | Settings -> Navigation.go(env.Navigator, settingsUrl)
          | Show migration ->
            selected(state, migration)
            tokenizeMigration(operations this, migration)
          | ToggleEdit ->
            state.Selected
            |> ValueOption.filter(fun _ -> selectedIsPending state)
            |> ValueOption.iter(fun _ ->
              if state.Editing then editEnded state else editStarted state)
          | ToggleNew ->
            state.NewOpen <- not state.NewOpen
            this.Rebuild()
          | SelectSection id ->
            state.Section <- id
            this.Rebuild()

        page(
          palette,
          state,
          project,
          nameField,
          onStepChange,
          (fun migration -> mountEditor(operations this, editors, migration)),
          onEvent
        )
  }
