module Migrondi.UI.Tests.AppDataTests

open System
open System.IO

open Xunit

open Migrondi.UI

let private tempDirectory() =
  Path.Combine(Path.GetTempPath(), $"migrondi-appdata-{Guid.NewGuid():N}")

[<Fact>]
let ``app data directory sits under local application data``() =
  let root =
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)

  Assert.Equal(Path.Combine(root, "MigrondiUI"), Paths.appDataDirectory())
  Assert.StartsWith(root, Paths.appDataDirectory())

[<Fact>]
let ``app database sits in the app data directory``() =
  Assert.Equal(
    Path.Combine(Paths.appDataDirectory(), "migrondi.db"),
    Paths.appDatabasePath()
  )

[<Fact>]
let ``migrateDatabase copies the legacy database and removes it``() =
  let directory = tempDirectory()
  Directory.CreateDirectory directory |> ignore
  let legacy = Path.Combine(directory, "migrondi.db")
  let target = Path.Combine(directory, "data", "migrondi.db")
  File.WriteAllText(legacy, "legacy")

  try
    let chosen = Paths.migrateDatabase legacy target

    Assert.Equal(target, chosen)
    Assert.Equal("legacy", File.ReadAllText target)
    Assert.False(File.Exists legacy)
  finally
    Directory.Delete(directory, true)

[<Fact>]
let ``migrateDatabase keeps an existing target and the legacy file``() =
  let directory = tempDirectory()
  Directory.CreateDirectory directory |> ignore
  let legacy = Path.Combine(directory, "migrondi.db")
  let target = Path.Combine(directory, "data", "migrondi.db")
  Directory.CreateDirectory(Path.GetDirectoryName target) |> ignore
  File.WriteAllText(legacy, "legacy")
  File.WriteAllText(target, "current")

  try
    let chosen = Paths.migrateDatabase legacy target

    Assert.Equal(target, chosen)
    Assert.Equal("current", File.ReadAllText target)
    Assert.True(File.Exists legacy)
  finally
    Directory.Delete(directory, true)

[<Fact>]
let ``migrateDatabase returns the target when no legacy database exists``() =
  let directory = tempDirectory()
  Directory.CreateDirectory directory |> ignore
  let legacy = Path.Combine(directory, "migrondi.db")
  let target = Path.Combine(directory, "data", "migrondi.db")

  try
    Assert.Equal(target, Paths.migrateDatabase legacy target)
    Assert.False(File.Exists target)
  finally
    Directory.Delete(directory, true)

[<Fact>]
let ``resolveDataDirectory moves a legacy directory``() =
  let directory = tempDirectory()
  let legacy = Path.Combine(directory, "roaming", "project")
  let target = Path.Combine(directory, "local", "project")
  Directory.CreateDirectory legacy |> ignore
  File.WriteAllText(Path.Combine(legacy, "migrondi.db"), "data")

  try
    let chosen = Paths.resolveDataDirectory legacy target

    Assert.Equal(target, chosen)
    Assert.Equal("data", File.ReadAllText(Path.Combine(target, "migrondi.db")))
    Assert.False(Directory.Exists legacy)
  finally
    Directory.Delete(directory, true)

[<Fact>]
let ``resolveDataDirectory creates an empty directory when no legacy exists``
  ()
  =
  let directory = tempDirectory()
  let target = Path.Combine(directory, "local", "project")

  try
    Assert.Equal(
      target,
      Paths.resolveDataDirectory (Path.Combine(directory, "missing")) target
    )

    Assert.True(Directory.Exists target)
  finally
    Directory.Delete(directory, true)
