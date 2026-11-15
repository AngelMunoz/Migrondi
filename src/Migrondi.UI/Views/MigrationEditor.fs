module Migrondi.UI.Views.MigrationEditor

open System

open Goo
open Goo.Widgets
open Goo.Widgets.Inputs
open Goo.Widgets.Layout
open FunGoo.Children
open FunGoo.Interop
open FunGoo.Style
open FunGoo.Widgets

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Migrondi.Core
open MigrondiUI.Services
open Migrondi.UI.Types
open Migrondi.UI.Views.Common

// The editor state: the draft plus the migration it was taken from. Dirty
// compares the two, so Done can guard and the hint can report it.
type EditorState = {
  Origin: Migration
  mutable Up: string
  mutable Down: string
  mutable ManualTransaction: bool
  mutable Busy: bool
}

// Data-only input: everything the editor needs except the answer channel.
// Outcomes travel over the `onOutcome` callback the factory takes.
type EditorArgs = {
  PostUI: IPostUI
  Feedback: IFeedback
  Ops: IMigrationOperations
  Theme: ThemeStore
  Migration: Migration
}

// What the editor reports to the page that mounted it. The page reloads the
// list, reselects, and rebuilds; always raised on the UI thread.
type EditorOutcome =
  | Saved of Migration
  | Deleted
  | Cancelled

// One event channel for the editor's own actions; the cell dispatches it.
type EditorEvent =
  | Save
  | Remove
  | Done

// True when the draft differs from the stored migration.
let isDirty(state: EditorState) : bool =
  state.Up <> state.Origin.upContent
  || state.Down <> state.Origin.downContent
  || state.ManualTransaction <> state.Origin.manualTransaction

// What an operation needs: the cell to rebuild, the marshaller to return
// through, the draft to save, the migration operations to drive, and the
// outcome channel to the page. One carrier so the operations below stay
// one-parameter functions.
type Operations = {
  Editor: Cell
  PostUI: IPostUI
  Feedback: IFeedback
  Ops: IMigrationOperations
  Origin: Migration
  State: EditorState
  OnOutcome: EditorOutcome -> unit
}

// Background operations: the cell arrives as a parameter, so these stay
// plain functions and the object expression stays non-recursive.
let save(operations: Operations) : unit =
  if operations.State.Busy then
    ()
  else
    operations.State.Busy <- true

    asyncEx {
      let draft = {
        operations.Origin with
            upContent = operations.State.Up
            downContent = operations.State.Down
            manualTransaction = operations.State.ManualTransaction
      }

      try
        let! result = operations.Ops.UpdateMigration draft

        match result with
        | Ok() ->
          operations.PostUI.Post(fun () ->
            operations.Feedback.Toast $"Saved {draft.name}"
            operations.OnOutcome(Saved draft))
        | Error(MigrationCrudError.AlreadyApplied name) ->
          operations.PostUI.Post(fun () ->
            operations.State.Busy <- false
            operations.Feedback.Toast $"Cannot save {name}: already applied"
            operations.Editor.Rebuild())
        | Error(MigrationCrudError.NotFound name) ->
          operations.PostUI.Post(fun () ->
            operations.State.Busy <- false
            operations.Feedback.Toast $"Cannot save {name}: not found"
            operations.Editor.Rebuild())
        | Error(MigrationCrudError.DatabaseError message) ->
          operations.PostUI.Post(fun () ->
            operations.State.Busy <- false
            operations.Feedback.Toast $"Cannot save: {message}"
            operations.Editor.Rebuild())
      with ex ->
        operations.PostUI.Post(fun () ->
          operations.State.Busy <- false
          operations.Feedback.Toast $"Save failed: {ex.Message}"
          operations.Editor.Rebuild())
    }
    |> Async.StartImmediate

// Deletes the pending migration after a confirmation. The engine refuses
// applied migrations (AlreadyApplied); that outcome surfaces as a toast.
let remove(operations: Operations) : unit =
  if operations.State.Busy then
    ()
  else
    operations.Feedback.Confirm(
      $"Delete migration {operations.Origin.name}?",
      "Delete",
      fun () ->
        operations.State.Busy <- true
        operations.Editor.Rebuild()

        asyncEx {
          try
            let! result = operations.Ops.DeleteMigration operations.Origin.name

            match result with
            | Ok() ->
              operations.PostUI.Post(fun () ->
                operations.Feedback.Toast $"Deleted {operations.Origin.name}"
                operations.OnOutcome Deleted)
            | Error(MigrationCrudError.AlreadyApplied name) ->
              operations.PostUI.Post(fun () ->
                operations.State.Busy <- false

                operations.Feedback.Toast
                  $"Cannot delete {name}: already applied"

                operations.Editor.Rebuild())
            | Error(MigrationCrudError.NotFound name) ->
              operations.PostUI.Post(fun () ->
                operations.State.Busy <- false
                operations.Feedback.Toast $"Cannot delete {name}: not found"
                operations.Editor.Rebuild())
            | Error(MigrationCrudError.DatabaseError message) ->
              operations.PostUI.Post(fun () ->
                operations.State.Busy <- false
                operations.Feedback.Toast $"Cannot delete: {message}"
                operations.Editor.Rebuild())
          with ex ->
            operations.PostUI.Post(fun () ->
              operations.State.Busy <- false
              operations.Feedback.Toast $"Delete failed: {ex.Message}"
              operations.Editor.Rebuild())
        }
        |> Async.StartImmediate
    )

// Leaving with a dirty draft asks first; a clean draft just reports back.
let cancel(operations: Operations) : unit =
  if operations.State.Busy then
    ()
  elif isDirty operations.State then
    operations.Feedback.Confirm(
      "Discard unsaved changes?",
      "Discard",
      fun () -> operations.OnOutcome Cancelled
    )
  else
    operations.OnOutcome Cancelled

// View pieces: plain functions over the palette and state, one event
// callback each.
let editorBar
  (palette: Palette, state: EditorState, onEvent: EditorEvent -> unit)
  : Blob =
  let hint =
    if state.Busy then
      "Working..."
    elif isDirty state then
      "Manual transaction · unsaved changes"
    else
      "Manual transaction"

  Container(FlexDirection = FlexDirection.Row, Gap = 8)
    .Children(
      ToggleSwitch(
        Value = state.ManualTransaction,
        AccessibilityName = "Manual transaction",
        OnChange = fun value -> state.ManualTransaction <- value
      )
        .Build(),
      Text(
        Content = hint,
        FontSize = 12,
        Color = palette.TextMuted,
        FlexGrow = 1.0
      ),
      actionButtonDisabled(
        palette,
        "Save",
        state.Busy,
        (fun () -> onEvent Save)
      ),
      actionButtonDisabled(
        palette,
        "Done",
        state.Busy,
        (fun () -> onEvent Done)
      )
    )

// An editor cell the details page mounts: the abstract member keeps the
// cell an object expression while the page feeds the appearance through
// `SetDark` and the draft survives theme flips.
[<AbstractClass>]
type MigrationEditorCell() =
  inherit Cell()

  /// Repaints the editor for the appearance; the draft survives.
  abstract SetDark: bool -> unit

// The editor owns its draft (state ladder tier 2). Keystrokes rebuild this
// cell only; the page that mounted it never sees the keystrokes.
let create
  (args: EditorArgs, onOutcome: EditorOutcome -> unit)
  : MigrationEditorCell =
  let state = {
    Origin = args.Migration
    Up = args.Migration.upContent
    Down = args.Migration.downContent
    ManualTransaction = args.Migration.manualTransaction
    Busy = false
  }

  // The appearance the editor paints with; the details page keeps it in
  // sync through `SetDark`.
  let mutable dark = args.Theme.Dark

  // Created once at cell birth: the mounted TextArea inputs carry the same
  // delegate instances on every Build.
  let onUpChanged = fun value -> state.Up <- value
  let onDownChanged = fun value -> state.Down <- value

  {
    new MigrationEditorCell() with
      override this.SetDark(next: bool) =
        if dark <> next then
          dark <- next
          this.Rebuild()

      override this.Build() : Blob =
        let palette = Palette.forTheme dark

        let operations: Operations = {
          Editor = this
          PostUI = args.PostUI
          Feedback = args.Feedback
          Ops = args.Ops
          Origin = args.Migration
          State = state
          OnOutcome = onOutcome
        }

        let onEvent =
          function
          | Save -> save operations
          | Remove -> remove operations
          | Done -> cancel operations

        Container(
          Key = "migration-editor",
          FlexDirection = FlexDirection.Column,
          Gap = 8
        )
          .Children(
            editorBar(palette, state, onEvent),
            actionButtonDisabled(
              palette,
              "Delete migration",
              state.Busy,
              (fun () -> onEvent Remove)
            ),
            Cell.Mount<TextAreaInput, TextArea>(
              "migration-editor-up",
              TextAreaInput(
                Value = state.Up,
                Label = "Migrate up",
                OnChange = onUpChanged,
                Height = Nullable(lengthFromFloat 200.0),
                BackgroundColor = Nullable palette.Panel,
                TextColor = Nullable palette.Text
              )
            ),
            Cell.Mount<TextAreaInput, TextArea>(
              "migration-editor-down",
              TextAreaInput(
                Value = state.Down,
                Label = "Migrate down",
                OnChange = onDownChanged,
                Height = Nullable(lengthFromFloat 200.0),
                BackgroundColor = Nullable palette.Panel,
                TextColor = Nullable palette.Text
              )
            )
          )
  }
