namespace MigrondiUI.Logic

open FSharp.Data.Adaptive

/// Update-style setters for adaptive values, same call shape the GUI code
/// has always used for `cval` state.
///
/// The GUI resolved these helpers from a UI-side package; this library
/// names no UI framework, so it carries its own.
[<AutoOpen>]
module internal AdaptiveStateExtensions =

  type ChangeableValue<'T> with

    member value.setValue(update: 'T -> 'T) : unit =
      transact(fun () -> value.Value <- update value.Value)

    member value.setValue(newValue: 'T) : unit =
      transact(fun () -> value.Value <- newValue)
