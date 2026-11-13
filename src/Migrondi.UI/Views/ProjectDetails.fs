module Migrondi.UI.Views.ProjectDetails

open System
open System.Collections.Generic
open System.Threading.Tasks

open Goo
open Goo.Widgets
open Goo.Widgets.Content
open Goo.Widgets.Feedback
open Goo.Widgets.Inputs
open Goo.Widgets.Layout
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
  mutable Busy: bool
  mutable NewName: string
  mutable Selected: Migration voption
  mutable Spans: MigrationSpans voption
  mutable Editing: bool
  mutable EditUp: string
  mutable EditDown: string
  mutable EditManualTransaction: bool
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
  | Create
  | DeleteProject
  | Show of Migration
  | ToggleEdit
  | SaveEdit
  | RemoveMigration

// Status chip colors that read on both appearances: saturated backgrounds
// with a near-white foreground.
let statusBadge(status: MigrationStatus) : Blob =
  let label, background =
    match status with
    | Applied _ -> "Applied", Color.Rgb(35, 74, 52)
    | Pending _ -> "Pending", Color.Rgb(89, 66, 35)

  Badge(
    Content = label,
    BackgroundColor = Nullable background,
    TextColor = Nullable(Color.Rgb(235, 240, 248))
  )
    .Build()

// One row per migration, with its database status. Clicking selects it.
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

  ListRow(
    Title = migration.name,
    Caption = $"timestamp {migration.timestamp}",
    Trailing = statusBadge status,
    OnClick = (fun _ -> onEvent(Show migration)),
    Selected = (selected = ValueSome migration),
    SelectedBackgroundColor = Nullable palette.RowSelected,
    MinHeight = Nullable(lengthFromFloat 40.0),
    PaddingVertical = Nullable 6.0
  )
    .backgroundColor(palette.Row)
    .textColor(palette.Text)
    .Build()

// State transitions: plain functions, callable without a window.
let listed(state: DetailsState, found: MigrationStatus array) =
  state.Statuses <- found
  state.Busy <- false

// The right pane shows the latest result: an operation that reports into the
// output pane releases the selected migration's code view, as the old app's
// pane switched to the dry-run result. Clicking a row selects it again.
let paneReleased(state: DetailsState) =
  state.Selected <- ValueNone
  state.Spans <- ValueNone

let shown(state: DetailsState, text: string) =
  paneReleased state
  state.Output <- text
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

// Entering edit mode copies the pending migration's content into the draft
// fields; leaving it keeps the stored content untouched.
let editStarted(state: DetailsState, migration: Migration) =
  state.Editing <- true
  state.EditUp <- migration.upContent
  state.EditDown <- migration.downContent
  state.EditManualTransaction <- migration.manualTransaction

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
  state.Output <- $"{context} failed: {ex.Message}"
  state.Busy <- false

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

      let text =
        preview
        |> Seq.map(fun m ->
          let content = if up then m.upContent else m.downContent

          $"-- {m.timestamp}_{m.name}.sql\n{content}")
        |> String.concat "\n\n"

      let output =
        if String.IsNullOrEmpty text then
          $"{heading}: nothing"
        else
          $"{heading}:\n{text}"

      let toast =
        match amount with
        | Some limit ->
          $"{heading} {min limit (Seq.length preview)} of {Seq.length preview} migrations"
        | None -> $"{heading} {Seq.length preview} migrations"

      operations.PostUI.Post(fun () ->
        shown(operations.State, output)
        operations.Feedback.Toast toast
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
// Saves the draft over the selected pending migration. The engine refuses
// applied migrations (AlreadyApplied); that DU case surfaces as a toast and
// the edit state stays. A successful save reloads the list and re-selects
// the migration so it tokenizes fresh.
let saveMigrationEdit(operations: Operations) : unit =
  asyncEx {
    match operations.State.Selected with
    | ValueNone -> ()
    | ValueSome current ->
      let migration = {
        current with
            upContent = operations.State.EditUp
            downContent = operations.State.EditDown
            manualTransaction = operations.State.EditManualTransaction
      }

      operations.State.Busy <- true

      try
        let! ct = Async.CancellationToken
        let! result = operations.Ops.UpdateMigration migration

        match result with
        | Ok() ->
          let! found = operations.Ops.Core.MigrationsListAsync ct
          let items = Array.ofSeq found

          let fresh =
            items
            |> Array.tryPick(function
              | Pending p when p.name = migration.name -> Some p
              | _ -> None)

          operations.PostUI.Post(fun () ->
            listed(operations.State, items)
            editEnded(operations.State)

            match fresh with
            | Some updated -> selected(operations.State, updated)
            | None -> operations.State.Selected <- ValueNone

            operations.Feedback.Toast $"Saved {migration.name}"
            operations.Cell.Rebuild())

          match fresh with
          | Some updated -> tokenizeMigration(operations, updated)
          | None -> ()
        | Error(MigrationCrudError.AlreadyApplied name) ->
          operations.PostUI.Post(fun () ->
            operations.State.Busy <- false
            operations.Feedback.Toast $"Cannot save {name}: already applied"
            operations.Cell.Rebuild())
        | Error(MigrationCrudError.NotFound name) ->
          operations.PostUI.Post(fun () ->
            operations.State.Busy <- false
            operations.Feedback.Toast $"Cannot save {name}: not found"
            operations.Cell.Rebuild())
        | Error(MigrationCrudError.DatabaseError message) ->
          operations.PostUI.Post(fun () ->
            operations.State.Busy <- false
            operations.Feedback.Toast $"Cannot save: {message}"
            operations.Cell.Rebuild())
      with ex ->
        operations.PostUI.Post(fun () ->
          failed(operations.State, "Saving the migration", ex)
          operations.Feedback.Toast $"Save failed: {ex.Message}"
          operations.Cell.Rebuild())
  }
  |> Async.Start

// Deletes the selected pending migration after a confirmation; the
// selection clears, the list reloads, and a toast reports the outcome.
let removeMigration(operations: Operations) : unit =
  match operations.State.Selected with
  | ValueNone -> ()
  | ValueSome current ->
    operations.Feedback.Confirm(
      $"Delete migration {current.name}?",
      "Delete",
      fun () ->
        asyncEx {
          try
            let! ct = Async.CancellationToken
            let! result = operations.Ops.DeleteMigration current.name

            match result with
            | Ok() ->
              let! found = operations.Ops.Core.MigrationsListAsync ct
              let items = Array.ofSeq found

              operations.PostUI.Post(fun () ->
                operations.State.Selected <- ValueNone
                operations.State.Spans <- ValueNone
                editEnded(operations.State)
                listed(operations.State, items)
                operations.Feedback.Toast $"Deleted {current.name}"
                operations.Cell.Rebuild())
            | Error(MigrationCrudError.AlreadyApplied name) ->
              operations.PostUI.Post(fun () ->
                operations.Feedback.Toast
                  $"Cannot delete {name}: already applied"

                operations.Cell.Rebuild())
            | Error(MigrationCrudError.NotFound name) ->
              operations.PostUI.Post(fun () ->
                operations.Feedback.Toast $"Cannot delete {name}: not found"
                operations.Cell.Rebuild())
            | Error(MigrationCrudError.DatabaseError message) ->
              operations.PostUI.Post(fun () ->
                operations.Feedback.Toast $"Cannot delete: {message}"
                operations.Cell.Rebuild())
          with ex ->
            operations.PostUI.Post(fun () ->
              failed(operations.State, "Deleting the migration", ex)
              operations.Feedback.Toast $"Delete failed: {ex.Message}"
              operations.Cell.Rebuild())
        }
        |> Async.Start
    )


/// A details page the shell starts: the abstract member keeps the cell an
/// object expression while outside code gets a callable entry point.
[<AbstractClass>]
type DetailsCell() =
  inherit Cell()

  /// Creates the tracking table and kicks the first migration listing.
  abstract Start: unit -> unit

// View pieces: plain functions over the palette and state, one event
// callback each.
let header(palette: Palette, project: Project) : Blob =
  let title, caption =
    match project with
    | Local p -> p.name, p.migrondiConfigPath
    | Virtual p -> p.name, p.connection

  Container(
    Key = "details-header",
    FlexDirection = FlexDirection.Column,
    Gap = 2
  )
    .Children(
      Text(Content = title, FontSize = 20, Color = palette.Text),
      Text(Content = caption, FontSize = 12, Color = palette.TextMuted)
    )

let toolbar
  (
    palette: Palette,
    state: DetailsState,
    onStepChange: Action<float>,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  // The limit toggle and the step count default to all migrations; the
  // number input only feeds the engine while the toggle is on.
  Container(
    Key = "details-toolbar",
    FlexDirection = FlexDirection.Row,
    Gap = 8,
    AlignItems = AlignItems.Center
  )
    .Children(
      actionButton(palette, "Dry Up", fun () -> onEvent DryUp),
      actionButton(palette, "Dry Down", fun () -> onEvent DryDown),
      actionButton(palette, "Apply", fun () -> onEvent Apply),
      actionButton(palette, "Rollback", fun () -> onEvent Rollback),
      ToggleSwitch(
        Value = state.LimitSteps,
        AccessibilityName = "Limit migrations",
        OnChange = fun limit -> state.LimitSteps <- limit
      )
        .Build(),
      Text(Content = "Limit", FontSize = 12, Color = palette.TextMuted),
      Cell.Mount<NumberInputInput, NumberInput>(
        "details-step",
        NumberInputInput(
          Value = state.Step,
          Minimum = Nullable 1.0,
          Step = Nullable 1.0,
          ShowSpinButtons = Nullable true,
          Disabled = not state.LimitSteps,
          Width = Nullable(lengthFromFloat 110.0),
          AccessibilityName = "Migration count",
          OnChange = onStepChange,
          OnCommit = onStepChange
        )
      ),
      Text(
        Content = (if state.Busy then "Working..." else ""),
        FontSize = 12,
        Color = palette.TextMuted,
        FlexGrow = 1.0
      )
    )

let newMigrationBar
  (palette: Palette, state: DetailsState, onEvent: DetailsEvent -> unit)
  : Blob =
  Container(
    Key = "details-new",
    FlexDirection = FlexDirection.Row,
    Gap = 8,
    AlignItems = AlignItems.Center
  )
    .Children(
      Container(FlexGrow = 1.0, MinWidth = 0)
        .Children(
          TextField(
            Placeholder = "new migration name",
            Value = state.NewName,
            OnChange = fun value -> state.NewName <- value
          )
            .Build()
        ),
      actionButton(palette, "New migration", fun () -> onEvent Create)
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

let outputPane(palette: Palette, state: DetailsState) : Blob =
  Container(
    FlexGrow = 1.0,
    FlexShrink = 1.0,
    MinWidth = 0,
    Padding = 12,
    BackgroundColor = palette.Panel,
    BorderRadius = 8,
    BorderWidth = edgesFromFloat 1.0,
    BorderColor = palette.Border
  )
    .Children(
      scrollColumn(
        palette,
        "details-output",
        [|
          Text(
            Key = "details-output-text",
            Content = state.Output,
            FontSize = 13,
            Color = palette.Text
          )
        |]
      )
    )

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
        ShowLineNumbers = true
      )
    | ValueNone ->
      CodeBlockInput(
        Code = section.Content,
        Language = section.Label,
        ShowLineNumbers = true
      )

  Cell.Mount<CodeBlockInput, CodeBlock>(section.Key, input)

// The right pane: the selected migration's up and down sections once one
// is selected, otherwise the dry-run output as before. Spans painted for
// another palette are ignored, so a theme switch shows plain content until
// the migration is selected again.
let migrationPane
  (
    palette: Palette,
    state: DetailsState,
    onEditUp: Action<string>,
    onEditDown: Action<string>,
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

    // While editing a pending migration the sections are controlled
    // TextAreas; otherwise they are the highlighted read-only panes.
    let sections: Blob array =
      if editing then
        [|
          Container(FlexDirection = FlexDirection.Row, Gap = 8)
            .Children(
              ToggleSwitch(
                Value = state.EditManualTransaction,
                AccessibilityName = "Manual transaction",
                OnChange = fun value -> state.EditManualTransaction <- value
              )
                .Build(),
              Text(
                Content = "Manual transaction",
                FontSize = 12,
                Color = palette.TextMuted,
                FlexGrow = 1.0
              ),
              actionButton(palette, "Save", fun () -> onEvent SaveEdit),
              actionButton(palette, "Done", fun () -> onEvent ToggleEdit)
            )
          Cell.Mount<TextAreaInput, TextArea>(
            "details-edit-up",
            TextAreaInput(
              Value = state.EditUp,
              Label = "Migrate up",
              OnChange = onEditUp,
              Height = Nullable(lengthFromFloat 200.0),
              BackgroundColor = Nullable palette.Panel,
              TextColor = Nullable palette.Text
            )
          )
          Cell.Mount<TextAreaInput, TextArea>(
            "details-edit-down",
            TextAreaInput(
              Value = state.EditDown,
              Label = "Migrate down",
              OnChange = onEditDown,
              Height = Nullable(lengthFromFloat 200.0),
              BackgroundColor = Nullable palette.Panel,
              TextColor = Nullable palette.Text
            )
          )
        |]
      else
        let actions: Blob array =
          if pending then
            [|
              actionButton(palette, "Edit", fun () -> onEvent ToggleEdit)

              actionButton(palette, "Delete", fun () -> onEvent RemoveMigration)
            |]
          else
            [||]

        let hint =
          Text(
            Content =
              (if pending then "This migration is still pending" else ""),
            FontSize = 12,
            Color = palette.TextMuted,
            FlexGrow = 1.0
          )
          :> Blob

        let row =
          Container(FlexDirection = FlexDirection.Row, Gap = 8).Children [|
            hint
            yield! actions
          |]

        [|
          row
          codePane {
            Key = "details-content-up"
            Label = "up · SQL"
            Content = migration.upContent
            Spans = upSpans
          }
          codePane {
            Key = "details-content-down"
            Label = "down · SQL"
            Content = migration.downContent
            Spans = downSpans
          }
        |]

    Container(
      Key = "details-content",
      FlexGrow = 1.0,
      FlexShrink = 1.0,
      MinWidth = 0,
      Padding = 12,
      BackgroundColor = palette.Panel,
      BorderRadius = 8,
      BorderWidth = edgesFromFloat 1.0,
      BorderColor = palette.Border
    )
      .Children(scrollColumn(palette, "details-content-scroll", sections))

let body
  (
    palette: Palette,
    state: DetailsState,
    onEditUp: Action<string>,
    onEditDown: Action<string>,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  Container(
    Key = "details-body",
    FlexDirection = FlexDirection.Row,
    Gap = 12,
    FlexGrow = 1.0,
    FlexShrink = 1.0,
    MinHeight = 0
  )
    .Children(
      Container(FlexGrow = 1.0, FlexShrink = 1.0, MinWidth = 0)
        .Children(
          scrollColumn(
            palette,
            "details-migrations",
            migrationRows(palette, state, onEvent)
          )
        ),
      migrationPane(palette, state, onEditUp, onEditDown, onEvent)
    )

let page
  (
    palette: Palette,
    state: DetailsState,
    project: Project,
    management: Blob,
    onStepChange: Action<float>,
    onEditUp: Action<string>,
    onEditDown: Action<string>,
    onEvent: DetailsEvent -> unit
  ) : Blob =
  Container(
    Key = "project-details",
    FlexDirection = FlexDirection.Column,
    Gap = 12,
    Padding = 12,
    Width = percent 100.0,
    Height = percent 100.0,
    BackgroundColor = palette.Canvas
  )
    .Children(
      header(palette, project),
      toolbar(palette, state, onStepChange, onEvent),
      newMigrationBar(palette, state, onEvent),
      management,
      body(palette, state, onEditUp, onEditDown, onEvent)
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
    Output = "No operation run yet"
    Busy = false
    NewName = ""
    Selected = ValueNone
    Spans = ValueNone
    Editing = false
    EditUp = ""
    EditDown = ""
    EditManualTransaction = false
    LimitSteps = false
    Step = 1.0
  }

  let ops = env.Migrations.Create project

  // The SQL grammar lives in this closure, not in module state, so a test
  // builds the page with its own registry.
  let options = RegistryOptions(ThemeName.DarkPlus)

  let sql: IGrammar =
    Registry(options).LoadGrammar(options.GetScopeByExtension(".sql"))

  // Created once at cell birth: the mounted TextArea and NumberInput inputs
  // carry the same delegate instances on every Build.
  let onEditUp = fun value -> state.EditUp <- value
  let onEditDown = fun value -> state.EditDown <- value
  let onStepChange = fun value -> state.Step <- value

  let operations(this: Cell) : Operations = {
    Cell = this
    PostUI = env.PostUI
    State = state
    Ops = ops
    Sql = sql
    Theme = env.Theme.Store
    Feedback = env.Feedback
  }

  // Created once, beside the cell it mounts: the settings section keeps its
  // form state across rebuilds and feeds on `SetDark` for theme flips.
  let settings =
    match project with
    | Virtual vp -> ValueSome(ProjectSettingsCell(env, vp))
    | Local _ -> ValueNone

  {
    new DetailsCell() with
      override this.Start() =
        // The tracking table exists before the first listing.
        ops.Core.Initialize()
        reload(operations this)

      override this.Build() : Blob =
        let dark = env.Theme.Store.Dark
        let palette = Palette.forTheme dark

        let onEvent =
          function
          | Back -> Navigation.go(env.Navigator, "/")
          | DryUp -> runDry(operations this, true)
          | DryDown -> runDry(operations this, false)
          | Apply -> run(operations this, true)
          | Rollback -> run(operations this, false)
          | Create -> createMigration(operations this)
          | DeleteProject -> ProjectSettings.deleteProject(this, env, project)
          | Show migration -> showMigration(operations this, migration)
          | ToggleEdit ->
            match state.Selected with
            | ValueSome migration when selectedIsPending state ->
              if state.Editing then
                editEnded state
              else
                editStarted(state, migration)
            | _ -> ()
          | SaveEdit -> saveMigrationEdit(operations this)
          | RemoveMigration -> removeMigration(operations this)

        // Virtual projects manage themselves through the mounted settings
        // section; local projects carry only the soft-delete action.
        let management: Blob =
          match settings with
          | ValueSome section ->
            section.SetDark dark

            Cell.Mount(
              Func<Cell>(fun () -> section :> Cell),
              "project-settings"
            )
          | ValueNone ->
            actionButton(
              palette,
              "Delete project",
              fun () -> onEvent DeleteProject
            )

        Container(
          Key = "project-details-root",
          FlexDirection = FlexDirection.Column,
          FlexGrow = 1.0,
          MinHeight = 0
        )
          .Children(
            actionButton(palette, "< Back", fun () -> onEvent Back),
            page(
              palette,
              state,
              project,
              management,
              onStepChange,
              onEditUp,
              onEditDown,
              onEvent
            )
          )
  }
