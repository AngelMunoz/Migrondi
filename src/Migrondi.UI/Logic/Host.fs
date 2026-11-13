namespace Migrondi.UI.Logic

/// Host file/directory picker seam. The UI layer implements it with its
/// platform picker; logic never sees a control.
type IFilePicker =
  /// Opens a picker filtered to migrondi.json files. Returns the picked file's local path.
  abstract PickProjectConfig: unit -> Async<string option>
  /// Opens a directory picker. Returns the picked directory's local path.
  abstract PickDirectory: unit -> Async<string option>
