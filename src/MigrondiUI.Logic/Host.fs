namespace MigrondiUI.Logic

open System
open MigrondiUI

/// Navigation seam. Logic requests route changes through this; the UI layer
/// implements it against its real router and maps errors to strings.
type INavigator =
  /// Navigate by URL, e.g. "/projects/local/<guid>".
  abstract Navigate: url: string -> Async<Result<unit, string>>
  /// Navigate by route name, e.g. "landing".
  abstract NavigateByName: name: string -> Async<Result<unit, string>>

/// Host file/directory picker seam. The UI layer implements it with its
/// platform picker; logic never sees a control.
type IFilePicker =
  /// Opens a picker filtered to migrondi.json files. Returns the picked file's local path.
  abstract PickProjectConfig: unit -> Async<string option>
  /// Opens a directory picker. Returns the picked directory's local path.
  abstract PickDirectory: unit -> Async<string option>

[<RequireQualifiedAccess>]
module Routes =
  let LandingRouteName = "landing"
  let localProjectUrl(projectId: Guid) = $"/projects/local/{projectId}"
  let virtualProjectUrl(projectId: Guid) = $"/projects/virtual/{projectId}"
