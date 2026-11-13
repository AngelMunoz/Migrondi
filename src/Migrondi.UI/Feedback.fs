module Migrondi.UI.Feedback

open System

open Goo
open Goo.Widgets.Feedback
open Goo.Widgets.Layout
open FunGoo.Children

open Migrondi.UI.Types

/// The toast and confirm surface. The shell mounts it once beside the page
/// content under a stable key; hosts are never disposed inside Build. Pages
/// reach the same surface through the `IFeedback` capability on the env.
[<AbstractClass>]
type FeedbackCell() =
  inherit Cell()

  /// Gives the surface the window the toast host needs for overlays and
  /// timers. Call once after the window exists, like the marshaller's
  /// attach.
  abstract Attach: Window -> unit

  abstract ShowToast: message: string -> unit

  abstract AskConfirm:
    message: string * confirmLabel: string * onConfirm: (unit -> unit) -> unit

  interface IFeedback with
    member this.Toast(message) = this.ShowToast(message)

    member this.Confirm(message, label, onConfirm) =
      this.AskConfirm(message, label, onConfirm)

// The surface owns its slice of app state (state ladder tier 2): the open
// toasts and the pending confirmation. Background calls arrive through the
// marshaller, so the mutable writes always run on the UI thread.
let create(post: IPostUI) : FeedbackCell =
  let mutable toasts = Array.empty<ToastItem>
  let mutable nextId = 0
  let mutable confirmMessage = ""
  let mutable confirmLabel = ""
  let mutable confirmAction: (unit -> unit) voption = ValueNone
  let mutable confirmOpen = false

  // The window slot the Goo lifecycle forces: the toast host input carries
  // the window, and the window is built after the cells. Attach once after
  // the window exists.
  let mutable window: Window voption = ValueNone

  // The mounted cell, so the cached dismissal delegate can refresh the
  // surface. Build fills it; nothing reads it before the first mount.
  let mutable mounted: FeedbackCell voption = ValueNone

  let closeConfirm() =
    confirmAction <- ValueNone
    confirmOpen <- false

  // Created once, outside Build: every host input snapshot carries the same
  // delegate instance, so an unchanged input skips the rebuild. The host
  // calls it on the UI thread after the toast finished its exit.
  let onDismiss =
    fun id ->
      toasts <- toasts |> Array.filter(fun item -> item.Id <> id)
      mounted |> ValueOption.iter(fun cell -> cell.Rebuild())

  let onConfirm =
    fun () ->
      let action = confirmAction
      closeConfirm()
      mounted |> ValueOption.iter(fun cell -> cell.Rebuild())
      action |> ValueOption.iter(fun run -> run())

  let onCancel =
    fun () ->
      closeConfirm()
      mounted |> ValueOption.iter(fun cell -> cell.Rebuild())

  let onOpenChange =
    fun requested ->
      if not requested then
        closeConfirm()
        mounted |> ValueOption.iter(fun cell -> cell.Rebuild())

  let toastHost() : Blob =
    let input =
      match window with
      | ValueSome current ->
        ToastHostInput(
          Window = current,
          Items = toasts,
          OnDismiss = onDismiss,
          Placement = ToastPlacement.BottomRight
        )
      | ValueNone ->
        // No window yet, so no toast can exist: every toast lands through a
        // post, and posts before the attach are dropped.
        ToastHostInput(
          Window = Unchecked.defaultof<Window>,
          Items = Array.empty,
          OnDismiss = onDismiss,
          Placement = ToastPlacement.BottomRight
        )

    Cell.Mount<ToastHostInput, ToastHost>("feedback-toasts", input)

  let confirmDialog() : Blob =
    let header = Text(Content = confirmMessage) :> Blob

    let dialog =
      ModalDialog(
        Open = confirmOpen,
        Preset = ModalDialogPreset.Confirm,
        Header = header,
        ConfirmText = confirmLabel,
        OnConfirm = onConfirm,
        OnCancel = onCancel,
        OnOpenChange = onOpenChange
      )

    Cell.Mount<ModalDialog, ModalDialogHost>("feedback-dialog", dialog)

  {
    new FeedbackCell() with
      override this.Attach(current: Window) =
        window <- ValueSome current
        this.Rebuild()

      override this.ShowToast(message: string) =
        // Background callers arrive here on foreign threads; the surface's
        // state and its rebuild stay on the UI thread.
        post.Post(fun () ->
          nextId <- nextId + 1

          toasts <-
            Array.append toasts [|
              ToastItem(Id = $"toast-{nextId}", Title = message)
            |]

          mounted |> ValueOption.iter(fun cell -> cell.Rebuild()))

      override this.AskConfirm(message, label, onConfirm) =
        post.Post(fun () ->
          confirmMessage <- message
          confirmLabel <- label
          confirmAction <- ValueSome onConfirm
          confirmOpen <- true
          mounted |> ValueOption.iter(fun cell -> cell.Rebuild()))

      override this.Build() : Blob =
        mounted <- ValueSome this

        Container(Key = "feedback").Children(toastHost(), confirmDialog())
  }
