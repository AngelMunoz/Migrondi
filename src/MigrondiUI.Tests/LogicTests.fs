module LogicTests

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

open Xunit

open Microsoft.Extensions.Logging.Abstractions

open FSharp.Data.Adaptive
open IcedTasks

open Migrondi.Core
open MigrondiUI
open MigrondiUI.Services
open MigrondiUI.Logic
open MigrondiUI.Logic.Landing
open MigrondiUI.Logic.NewProject
open MigrondiUI.Logic.LocalProjectDetails
open MigrondiUI.Logic.VirtualProjectDetails
open MigrondiUI.Logic.MigrationRunner
open MigrondiUI.Logic.VirtualProjectState
open MigrondiUI.Tests.TestHelpers

let private createLocalProject() : LocalProject = {
  id = Guid.NewGuid()
  name = "local test project"
  description = None
  config = None
  migrondiConfigPath =
    Path.Combine(Path.GetTempPath(), "does-not-exist", "migrondi.json")
}

let private createVirtualProject() : VirtualProject = {
  id = Guid.NewGuid()
  name = "virtual test project"
  description = None
  connection = "Data Source=:memory:"
  tableName = "__migrondi_migrations"
  driver = MigrondiDriver.Sqlite
  projectId = Guid.NewGuid()
}

type FakeNavigator(?failure: string) =
  let mutable urls: string list = []
  let mutable names: string list = []

  member _.Urls: string list = List.rev urls

  member _.Names: string list = List.rev names

  interface INavigator with
    member _.Navigate(url: string) =
      urls <- url :: urls

      async {
        match failure with
        | Some failure -> return Error failure
        | None -> return Ok()
      }

    member _.NavigateByName(name: string) =
      names <- name :: names

      async {
        match failure with
        | Some failure -> return Error failure
        | None -> return Ok()
      }

type FakeFilePicker() =
  let mutable projectConfig: string option = None
  let mutable directory: string option = None

  member _.ProjectConfig
    with get () = projectConfig
    and set (value) = projectConfig <- value

  member _.Directory
    with get () = directory
    and set (value) = directory <- value

  interface IFilePicker with
    member _.PickProjectConfig() = async { return projectConfig }

    member _.PickDirectory() = async { return directory }

type FakeProjectCollection() =
  let mutable listResult: Project list = []
  let mutable registered: (string * string option) list = []
  let mutable imported: string list = []
  let mutable createdVirtual: Database.InsertVirtualProjectArgs list = []
  let mutable updatedVirtual: VirtualProject option = None

  let fixedId = Guid.Parse("11111111-1111-1111-1111-111111111111")

  member _.FixedId: Guid = fixedId

  member _.ListResult
    with get () = listResult
    and set (value) = listResult <- value

  member _.RegisteredConfigs: (string * string option) list =
    List.rev registered

  member _.ImportedPaths: string list = List.rev imported

  member _.CreatedVirtual: Database.InsertVirtualProjectArgs list =
    List.rev createdVirtual

  member _.UpdatedVirtual = updatedVirtual

  interface IProjectCollection with
    member _.List() = cancellableTask { return listResult }

    member _.Get(projectId: Guid) = cancellableTask { return None }

    member _.RegisterLocal(configPath: string, ?name: string) = cancellableTask {
      registered <- (configPath, name) :: registered
      return fixedId
    }

    member _.CreateVirtual(args: Database.InsertVirtualProjectArgs) =
      cancellableTask {
        createdVirtual <- args :: createdVirtual
        return fixedId
      }

    member _.UpdateVirtual(project: VirtualProject) = cancellableTask {
      updatedVirtual <- Some project
    }

    member _.Import(configPath: string) = cancellableTask {
      imported <- configPath :: imported
      return fixedId
    }

    member _.Export(projectId: Guid, targetPath: string) = cancellableTask {
      return targetPath
    }

    member _.DeleteProject(projectId: Guid, kind: DeleteKind) = cancellableTask {
      return Ok()
    }

type FakeMigrondi() =
  let mutable calls: string list = []
  let mutable migrationsListResult: MigrationStatus IReadOnlyList = [||]
  let mutable dryRunUpResult: Migration IReadOnlyList = [||]

  member _.Calls: string list = List.rev calls

  member _.MigrationsListResult
    with get () = migrationsListResult
    and set (value) = migrationsListResult <- value

  member _.DryRunUpResult
    with get () = dryRunUpResult
    and set (value) = dryRunUpResult <- value

  interface IMigrondi with
    member _.Initialize() = calls <- "Initialize" :: calls

    member _.InitializeAsync([<Optional>] ?cancellationToken) =
      calls <- "InitializeAsync" :: calls
      Task.CompletedTask

    member _.RunNew(friendlyName, ?up, ?down, ?manualTransaction) =
      failwith "not used by this test"

    member _.RunNewAsync
      (friendlyName, ?up, ?down, ?manualTransaction, ?cancellationToken)
      =
      calls <- "RunNewAsync" :: calls
      Task.FromResult(createTestMigration friendlyName)

    member _.RunUp(?amount) = failwith "not used by this test"

    member _.RunDown(?amount) = failwith "not used by this test"

    member _.DryRunUp(?amount) = failwith "not used by this test"

    member _.DryRunDown(?amount) = failwith "not used by this test"

    member _.MigrationsList() = failwith "not used by this test"

    member _.ScriptStatus(migrationPath) = failwith "not used by this test"

    member _.RunUpAsync([<Optional>] ?amount, [<Optional>] ?cancellationToken) =
      calls <- "RunUpAsync" :: calls
      Task.FromResult([||] :> MigrationRecord IReadOnlyList)

    member _.RunDownAsync
      ([<Optional>] ?amount, [<Optional>] ?cancellationToken)
      =
      calls <- "RunDownAsync" :: calls
      Task.FromResult([||] :> MigrationRecord IReadOnlyList)

    member _.DryRunUpAsync
      ([<Optional>] ?amount, [<Optional>] ?cancellationToken)
      =
      calls <- "DryRunUpAsync" :: calls
      Task.FromResult(dryRunUpResult)

    member _.DryRunDownAsync
      ([<Optional>] ?amount, [<Optional>] ?cancellationToken)
      =
      calls <- "DryRunDownAsync" :: calls
      Task.FromResult([||] :> Migration IReadOnlyList)

    member _.MigrationsListAsync([<Optional>] ?cancellationToken) =
      calls <- "MigrationsListAsync" :: calls
      Task.FromResult(migrationsListResult)

    member _.ScriptStatusAsync(migrationPath, [<Optional>] ?cancellationToken) =
      failwith "not used by this test"

type FakeMigrationOperations(core: IMigrondi) =
  let mutable updateResult = Ok()
  let mutable deleteResult = Ok()

  member _.UpdateResult
    with get () = updateResult
    and set (value) = updateResult <- value

  member _.DeleteResult
    with get () = deleteResult
    and set (value) = deleteResult <- value

  interface IMigrationOperations with
    member _.Core = core

    member _.GetMigration(name: string) = cancellableTask { return None }

    member _.UpdateMigration(migration: Migration) = cancellableTask {
      return updateResult
    }

    member _.DeleteMigration(name: string) = cancellableTask {
      return deleteResult
    }

type FakeMigrationOperationsFactory() =
  let core = FakeMigrondi()

  let operations =
    FakeMigrationOperations(core :> IMigrondi) :> IMigrationOperations

  member _.Core: FakeMigrondi = core

  member _.Operations: IMigrationOperations = operations

  interface IMigrationOperationsFactory with
    member _.Create(project: Project) = operations

[<Fact>]
let ``LandingVM - LoadProjects populates Projects``() =
  let local = createLocalProject()
  let virtualProject = createVirtualProject()
  let projects = FakeProjectCollection()
  projects.ListResult <- [ Local local; Virtual virtualProject ]

  let navigator = FakeNavigator()

  let vm =
    LandingVM(
      NullLogger<LandingVM>.Instance,
      projects :> IProjectCollection,
      navigator :> INavigator
    )

  vm.LoadProjects() |> Async.RunSynchronously

  Assert.Equal<Project list>(
    [ Local local; Virtual virtualProject ],
    vm.Projects |> AVal.force
  )

[<Fact>]
let ``LandingVM - OpenProject navigates to the local project url``() =
  let local = createLocalProject()
  let navigator = FakeNavigator()

  let vm =
    LandingVM(
      NullLogger<LandingVM>.Instance,
      FakeProjectCollection() :> IProjectCollection,
      navigator :> INavigator
    )

  vm.OpenProject(Local local) |> Async.RunSynchronously

  Assert.Equal<string list>([ Routes.localProjectUrl local.id ], navigator.Urls)

[<Fact>]
let ``LandingVM - OpenProject navigates to the virtual project url``() =
  let virtualProject = createVirtualProject()
  let navigator = FakeNavigator()

  let vm =
    LandingVM(
      NullLogger<LandingVM>.Instance,
      FakeProjectCollection() :> IProjectCollection,
      navigator :> INavigator
    )

  vm.OpenProject(Virtual virtualProject) |> Async.RunSynchronously

  Assert.Equal<string list>(
    [ Routes.virtualProjectUrl virtualProject.id ],
    navigator.Urls
  )

[<Fact>]
let ``LandingVM - OpenProject logs navigation failures without throwing``() =
  let local = createLocalProject()
  let navigator = FakeNavigator("boom")

  let vm =
    LandingVM(
      NullLogger<LandingVM>.Instance,
      FakeProjectCollection() :> IProjectCollection,
      navigator :> INavigator
    )

  vm.OpenProject(Local local) |> Async.RunSynchronously

  Assert.Equal<string list>([ Routes.localProjectUrl local.id ], navigator.Urls)

[<Fact>]
let ``NewProjectVM - SelectLocalProject CreateLocal registers the picked config``
  ()
  =
  let picker = FakeFilePicker()
  let projects = FakeProjectCollection()
  let navigator = FakeNavigator()

  let projectDir =
    Path.Combine(Path.GetTempPath(), "migrondi-ui-tests", "MyProject")

  let configPath = Path.Combine(projectDir, "migrondi.json")

  picker.ProjectConfig <- Some configPath

  let vm =
    NewProjectVM(
      NullLogger<NewProjectVM>.Instance,
      projects :> IProjectCollection,
      picker :> IFilePicker,
      navigator :> INavigator
    )

  vm.SelectLocalProject(LocalProjectTarget.CreateLocal)
  |> Async.RunSynchronously

  Assert.Equal<(string * string option) list>(
    [ (configPath, Some "MyProject") ],
    projects.RegisteredConfigs
  )

  Assert.Equal<string list>(
    [ Routes.localProjectUrl projects.FixedId ],
    navigator.Urls
  )

[<Fact>]
let ``NewProjectVM - SelectLocalProject with no picked file does nothing``() =
  let picker = FakeFilePicker()
  let projects = FakeProjectCollection()
  let navigator = FakeNavigator()

  let vm =
    NewProjectVM(
      NullLogger<NewProjectVM>.Instance,
      projects :> IProjectCollection,
      picker :> IFilePicker,
      navigator :> INavigator
    )

  vm.SelectLocalProject(LocalProjectTarget.CreateLocal)
  |> Async.RunSynchronously

  vm.SelectLocalProject(LocalProjectTarget.ImportToVirtual)
  |> Async.RunSynchronously

  Assert.Equal<(string * string option) list>([], projects.RegisteredConfigs)

  Assert.Equal<string list>([], projects.ImportedPaths)

  Assert.Equal<string list>([], navigator.Urls)

[<Fact>]
let ``NewProjectVM - SelectLocalProject ImportToVirtual imports and navigates to the virtual url``
  ()
  =
  let picker = FakeFilePicker()
  let projects = FakeProjectCollection()
  let navigator = FakeNavigator()

  let configPath =
    Path.Combine(
      Path.GetTempPath(),
      "migrondi-ui-tests",
      "Imported",
      "migrondi.json"
    )

  picker.ProjectConfig <- Some configPath

  let vm =
    NewProjectVM(
      NullLogger<NewProjectVM>.Instance,
      projects :> IProjectCollection,
      picker :> IFilePicker,
      navigator :> INavigator
    )

  vm.SelectLocalProject(LocalProjectTarget.ImportToVirtual)
  |> Async.RunSynchronously

  Assert.Equal<string list>([ configPath ], projects.ImportedPaths)

  Assert.Equal<string list>(
    [ Routes.virtualProjectUrl projects.FixedId ],
    navigator.Urls
  )

[<Fact>]
let ``NewProjectVM - CreateNewLocalProject writes the config and navigates``() =
  let picker = FakeFilePicker()
  let projects = FakeProjectCollection()
  let navigator = FakeNavigator()

  let directory =
    Path.Combine(
      Path.GetTempPath(),
      "migrondi-ui-tests",
      Guid.NewGuid().ToString("N")
    )

  Directory.CreateDirectory(directory) |> ignore

  try
    picker.Directory <- Some directory

    let vm =
      NewProjectVM(
        NullLogger<NewProjectVM>.Instance,
        projects :> IProjectCollection,
        picker :> IFilePicker,
        navigator :> INavigator
      )

    vm.CreateNewLocalProject() |> Async.RunSynchronously

    Assert.True(Directory.Exists(Path.Combine(directory, "migrations")))

    Assert.True(File.Exists(Path.Combine(directory, "migrondi.json")))

    let dirName = Path.GetFileNameWithoutExtension directory

    Assert.Equal<(string * string option) list>(
      [ (Path.Combine(directory, "migrondi.json"), Some dirName) ],
      projects.RegisteredConfigs
    )

    Assert.Equal<string list>(
      [ Routes.localProjectUrl projects.FixedId ],
      navigator.Urls
    )
  finally
    Directory.Delete(directory, true)

[<Fact>]
let ``NewProjectVM - CreateNewVirtualProject creates and navigates to the virtual url``
  ()
  =
  let projects = FakeProjectCollection()
  let navigator = FakeNavigator()

  let args: Database.InsertVirtualProjectArgs = {
    name = "virtual project"
    description = None
    connection = "Data Source=:memory:"
    tableName = "__migrondi_migrations"
    driver = "sqlite"
  }

  let vm =
    NewProjectVM(
      NullLogger<NewProjectVM>.Instance,
      projects :> IProjectCollection,
      FakeFilePicker() :> IFilePicker,
      navigator :> INavigator
    )

  vm.CreateNewVirtualProject args |> Async.RunSynchronously

  Assert.Equal<Database.InsertVirtualProjectArgs list>(
    [ args ],
    projects.CreatedVirtual
  )

  Assert.Equal<string list>(
    [ Routes.virtualProjectUrl projects.FixedId ],
    navigator.Urls
  )

[<Fact>]
let ``LocalProjectDetailsVM - RunMigrations DryUp sets LastDryRun and CurrentShow``
  ()
  =
  let migrations = [| createTestMigration "one"; createTestMigration "two" |]

  let factory = FakeMigrationOperationsFactory()
  factory.Core.DryRunUpResult <- (migrations :> Migration IReadOnlyList)

  let navigator = FakeNavigator()

  let vm =
    LocalProjectDetailsVM(
      NullLogger<LocalProjectDetailsVM>.Instance,
      FakeProjectCollection() :> IProjectCollection,
      factory :> IMigrationOperationsFactory,
      createLocalProject(),
      navigator :> INavigator
    )

  vm.RunMigrations(RunMigrationKind.DryUp, 2) |> Async.RunSynchronously

  Assert.Equal<Migration array>(migrations, vm.LastDryRun |> AVal.force)

  Assert.Equal(
    CurrentShow.DryRun RunMigrationKind.Up,
    vm.CurrentShow |> AVal.force
  )

[<Fact>]
let ``LocalProjectDetailsVM - NavigateBack goes to landing by name``() =
  let navigator = FakeNavigator()

  let vm =
    LocalProjectDetailsVM(
      NullLogger<LocalProjectDetailsVM>.Instance,
      FakeProjectCollection() :> IProjectCollection,
      FakeMigrationOperationsFactory() :> IMigrationOperationsFactory,
      createLocalProject(),
      navigator :> INavigator
    )

  vm.NavigateBack() |> Async.RunSynchronously

  Assert.Equal<string list>([ Routes.LandingRouteName ], navigator.Names)

[<Fact>]
let ``VirtualProjectDetailsVM - SaveMigration returns false on AlreadyApplied``
  ()
  =
  let factory = FakeMigrationOperationsFactory()

  let operations = factory.Operations :?> FakeMigrationOperations

  operations.UpdateResult <- Error(MigrationCrudError.AlreadyApplied "test")

  let migration = createTestMigration "test"

  let vm =
    VirtualProjectDetailsVM(
      NullLogger<VirtualProjectDetailsVM>.Instance,
      FakeProjectCollection() :> IProjectCollection,
      factory :> IMigrationOperationsFactory,
      createVirtualProject(),
      FakeNavigator() :> INavigator
    )

  let result = vm.SaveMigration migration |> Async.RunSynchronously

  Assert.False(result)

[<Fact>]
let ``VirtualProjectDetailsVM - DeleteMigration success reloads migrations``() =
  let factory = FakeMigrationOperationsFactory()

  let operations = factory.Operations :?> FakeMigrationOperations

  operations.DeleteResult <- Ok()

  let migration = createTestMigration "test"

  let vm =
    VirtualProjectDetailsVM(
      NullLogger<VirtualProjectDetailsVM>.Instance,
      FakeProjectCollection() :> IProjectCollection,
      factory :> IMigrationOperationsFactory,
      createVirtualProject(),
      FakeNavigator() :> INavigator
    )

  let result = vm.DeleteMigration migration |> Async.RunSynchronously

  Assert.True(result)

  // the view triggers the reload after a successful delete; the same flow at
  // the logic layer re-lists migrations through the operations' core
  vm.ListMigrations() |> Async.RunSynchronously

  Assert.True(factory.Core.Calls |> List.contains "MigrationsListAsync")

[<Fact>]
let ``VirtualProjectDetailsVM - UpdateProject refreshes the Project state``() =
  let projects = FakeProjectCollection()

  let original = createVirtualProject()
  let updated = { original with name = "updated name" }

  let vm =
    VirtualProjectDetailsVM(
      NullLogger<VirtualProjectDetailsVM>.Instance,
      projects :> IProjectCollection,
      FakeMigrationOperationsFactory() :> IMigrationOperationsFactory,
      original,
      FakeNavigator() :> INavigator
    )

  vm.UpdateProject updated |> Async.RunSynchronously

  Assert.Equal(Some updated, projects.UpdatedVirtual)

  Assert.Equal(updated, vm.Project |> AVal.force)

[<Fact>]
let ``VirtualProjectState - empty name is rejected, valid name updates the project``
  ()
  =
  let project = createVirtualProject() |> cval
  let original = project |> AVal.force

  setName "   " project
  Assert.Equal(original, project |> AVal.force)

  setName "renamed project" project

  let updated = project |> AVal.force

  Assert.Equal("renamed project", updated.name)

[<Fact>]
let ``Routes - local and virtual project urls have the expected format``() =
  let projectId = Guid.Parse("22222222-2222-2222-2222-222222222222")

  Assert.Equal(
    "/projects/local/22222222-2222-2222-2222-222222222222",
    Routes.localProjectUrl projectId
  )

  Assert.Equal(
    "/projects/virtual/22222222-2222-2222-2222-222222222222",
    Routes.virtualProjectUrl projectId
  )

  Assert.Equal("landing", Routes.LandingRouteName)
