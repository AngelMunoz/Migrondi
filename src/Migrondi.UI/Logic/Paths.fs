module Migrondi.UI.Paths

open System
open System.IO

[<Literal>]
let AppFolderName = "MigrondiUI"

/// The per-user folder for mutable app data. LocalApplicationData stays
/// writable inside a Flatpak sandbox and survives tool and app updates;
/// the install folder does neither.
let appDataDirectory() =
  let root =
    Environment.GetFolderPath(
      Environment.SpecialFolder.LocalApplicationData,
      Environment.SpecialFolderOption.Create
    )

  Path.Combine(root, AppFolderName)

let ensureAppDataDirectory() =
  let directory = appDataDirectory()
  Directory.CreateDirectory directory |> ignore
  directory

let appDatabasePath() =
  Path.Combine(appDataDirectory(), "migrondi.db")

/// Older builds kept the app database next to the executable.
let legacyAppDatabasePath() =
  Path.Combine(AppContext.BaseDirectory, "migrondi.db")

/// Older builds kept virtual project databases under roaming app data.
let legacyVirtualProjectDirectory(projectId: Guid) =
  let root =
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

  Path.Combine(root, AppFolderName, projectId.ToString())

/// Copies a database next to the executable to the app data folder and
/// returns the path to use. An existing target wins; a failed copy keeps
/// the legacy path so the app still starts.
let migrateDatabase (legacyPath: string) (targetPath: string) =
  if File.Exists targetPath then
    targetPath
  elif not(File.Exists legacyPath) then
    targetPath
  else
    try
      Path.GetDirectoryName targetPath
      |> Option.ofObj
      |> Option.iter(fun directory ->
        Directory.CreateDirectory directory |> ignore)

      let pending = targetPath + ".tmp"
      File.Copy(legacyPath, pending, true)
      File.Move(pending, targetPath, true)

      try
        File.Delete legacyPath
      with _ ->
        ()

      targetPath
    with _ ->
      try
        File.Delete(targetPath + ".tmp")
      with _ ->
        ()

      legacyPath

/// Uses an existing target directory, moves a legacy directory into place
/// when possible, and falls back to the legacy directory when the move
/// fails.
let resolveDataDirectory (legacyPath: string) (targetPath: string) =
  if Directory.Exists targetPath then
    targetPath
  elif not(Directory.Exists legacyPath) then
    Directory.CreateDirectory targetPath |> ignore
    targetPath
  else
    try
      Path.GetDirectoryName targetPath
      |> Option.ofObj
      |> Option.iter(fun directory ->
        Directory.CreateDirectory directory |> ignore)

      Directory.Move(legacyPath, targetPath)
      targetPath
    with _ ->
      legacyPath

let virtualProjectDirectory(projectId: Guid) =
  let target = Path.Combine(ensureAppDataDirectory(), projectId.ToString())

  resolveDataDirectory (legacyVirtualProjectDirectory projectId) target

/// The app database path, migrating one left next to the executable by an
/// older build or tool version.
let migrateLegacyDatabase() =
  ensureAppDataDirectory() |> ignore
  migrateDatabase (legacyAppDatabasePath()) (appDatabasePath())
