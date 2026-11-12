namespace Migrondi.Mcp

module ProjectInit =

  /// What the caller should do for a directory, given its state and force.
  type InitDecision =
    | CreateFresh // directory empty (or nonexistent): write defaults
    | AdoptExisting // non-empty + force: recover fields, rewrite, keep files
    | RefuseNonEmpty // non-empty, no force

  /// `true, _ -> CreateFresh | false, true -> AdoptExisting |
  /// false, false -> RefuseNonEmpty`. The destructive clean setup stays
  /// CLI-only: the shared layer never deletes a migrations directory.
  val decide: isEmpty: bool -> force: bool -> InitDecision

  /// True when the directory contains no files and no subdirectories;
  /// a missing directory is created and reported as empty.
  val isDirectoryEmpty: path: System.IO.DirectoryInfo -> bool

  /// Recovers known fields from arbitrary JSON (`Map<string,string>` view),
  /// filling gaps with `MigrondiConfig.Default` — the same recovery the
  /// CLI's `--force --merge` performs on partial or invalid files.
  val recoverConfig: values: Map<string, string> -> Migrondi.Core.MigrondiConfig

  /// Applies optional caller-provided fields on top of a base config. An
  /// invalid driver value falls back to the base config's driver; callers
  /// that must report invalid drivers as errors validate before calling.
  val applyOverrides:
    config: Migrondi.Core.MigrondiConfig ->
    overrides: ProjectOverrides ->
      Migrondi.Core.MigrondiConfig

  /// Writes migrondi.json (MiSerializer.Encode — byte-identical to the
  /// CLI's WriteConfiguration for the default physical source) and creates
  /// the migrations directory. Never deletes anything.
  val writeProject:
    directory: System.IO.DirectoryInfo ->
    config: Migrondi.Core.MigrondiConfig ->
      Result<unit, string>
