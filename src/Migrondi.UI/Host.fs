module Migrondi.UI.Host

open System

open Goo

open Migrondi.UI.Logic
open IcedTasks

/// The host file and directory picker seam over Goo's native dialogs.
/// `ShowFileDialogAsync` requires the UI thread, so the pick methods start
/// the dialog synchronously when called; call them from Build-authored
/// callbacks only, and start the returned async on any thread.
type FilePicker() =
  /// The window slot the Goo lifecycle forces: the window is built after
  /// the services. Attach once after the window exists.
  /// <remarks>This must be run in the UI thread, Use Async.StartImmediate.</remarks>
  let mutable window: Window voption = ValueNone
  /// A native chooser refuses a second modal child on the same window; one
  /// at a time keeps the reentry error away.
  /// <remarks>This must be run in the UI thread, Use Async.StartImmediate.</remarks>
  let mutable busy = false

  let pick
    (kind: FileDialogKind, options: FileDialogOptions)
    : Async<string option> =
    match window with
    | ValueNone -> async { return None }
    | ValueSome current ->
      if busy then
        async { return None }
      else
        asyncEx {
          busy <- true

          try
            let! result = current.ShowFileDialogAsync(kind, options)
            busy <- false

            return
              if
                result.Status = FileDialogStatus.Success
                && result.Paths.Count > 0
              then
                Some result.Paths[0]
              else
                None
          with _ ->
            busy <- false
            return None
        }

  member _.Attach(current: Window) = window <- ValueSome current

  interface IFilePicker with
    member this.PickProjectConfig() =
      let options =
        FileDialogOptions(
          Title = "Pick a migrondi.json",
          Filters = [| FileDialogFilter("migrondi.json", "json") |]
        )

      pick(FileDialogKind.OpenFile, options)

    member this.PickDirectory() =
      pick(FileDialogKind.Folder, FileDialogOptions(Title = "Pick a folder"))
