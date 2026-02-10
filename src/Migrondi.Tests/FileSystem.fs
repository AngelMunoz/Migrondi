module Migrondi.Tests.FileSystem

open System
open System.IO
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting


open Microsoft.Extensions.Logging

open Migrondi.Core
open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem

open FsToolkit.ErrorHandling

module MigrondiConfigData =

  [<Literal>]
  let directoryName = "fs-migrondi-config"

  [<Literal>]
  let fsMigrondiPath = directoryName + "/" + "migrondi.json"

  let fsMigrondiConfigPath (root: string) =
    Path.Combine(root, directoryName, "migrondi.json")

  let fsRelativeMigrondiConfigPath =
    Path.Combine(directoryName, "migrondi.json")

  let configSampleObject = {
    connection = "connection"
    migrations = "./migrations"
    tableName = "migrations"
    driver = MigrondiDriver.Sqlite
  }

module MigrationData =
  [<Literal>]
  let directoryName = "fs-migrations"

  let nameSchema = Text.RegularExpressions.Regex("(.+)_([0-9]+).(sql|SQL)")

  let getMigrationObjects (amount: int) = [
    for i in 1 .. amount + 1 do
      // ensure the timestamps are different
      let timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1L

      {
        name = $"AddTable{i}"
        upContent =
          "CREATE TABLE IF NOT EXISTS migration(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name VARCHAR(255) NOT NULL,
  timestamp BIGINT NOT NULL
);"
        downContent = $"DROP TABLE migration;"
        timestamp = timestamp
        manualTransaction = false
      },
      $"AddTable{i}_{timestamp}.sql"
  ]


[<TestClass>]
type FileSystemTests() =

  let baseUri =
    let tmp = Path.GetTempPath()

    let path =
      $"{Path.Combine(tmp, Guid.NewGuid().ToString())}%c{Path.DirectorySeparatorChar}"

    Uri(path, UriKind.Absolute)

  let rootDir = DirectoryInfo(baseUri.LocalPath)

  let loggerFactory =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.Debug).AddSimpleConsole() |> ignore
    )

  let logger = loggerFactory.CreateLogger("Migrondi:Tests.Database")


  let serializer = MigrondiSerializer()

  let fileSystem =
    MiFileSystem(
      logger,
      serializer,
      serializer,
      baseUri,
      Uri("fs-migrations/", UriKind.Relative)
    )
    :> IMiFileSystem

  do printfn $"Using '{rootDir.FullName}' as Root Directory"

  [<TestCleanup>]
  member _.TestCleanup() =
    // We're done with these tests remove any temporary files
    rootDir.Delete(true)
    printfn $"Deleted temporary root dir at: '{rootDir.FullName}'"


  [<TestMethod>]
  member _.``Can write a migrondi.json file``() =

    let expected =
      (serializer :> IMiConfigurationSerializer).Encode
        MigrondiConfigData.configSampleObject

    fileSystem.WriteConfiguration(
      MigrondiConfigData.configSampleObject,
      MigrondiConfigData.fsRelativeMigrondiConfigPath
    )

    let actual =
      let path = MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName
      File.ReadAllText path

    Assert.AreEqual<string>(expected, actual)

  [<TestMethod>]
  member _.``Can read a migrondi.json file``() =
    let expected =

      fileSystem.WriteConfiguration(
        MigrondiConfigData.configSampleObject,
        MigrondiConfigData.fsRelativeMigrondiConfigPath
      )

      File.ReadAllText(MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName)
      |> (serializer :> IMiConfigurationSerializer).Decode

    let fileResult =
      fileSystem.ReadConfiguration(
        MigrondiConfigData.fsRelativeMigrondiConfigPath
      )

    Assert.AreEqual(expected, fileResult)

  [<TestMethod>]
  member _.``Can write a migrondi.json file async``() =
    task {
      let expected =
        (serializer :> IMiConfigurationSerializer).Encode
          MigrondiConfigData.configSampleObject

      do!
        fileSystem.WriteConfigurationAsync(
          MigrondiConfigData.configSampleObject,
          MigrondiConfigData.fsRelativeMigrondiConfigPath
        )

      let! actual =
        let path = MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName
        File.ReadAllTextAsync path

      Assert.AreEqual<string>(expected, actual)
    }
    :> Task

  [<TestMethod>]
  member _.``Can read a migrondi.json file async``() =
    task {
      let! expected = task {
        do!
          fileSystem.WriteConfigurationAsync(
            MigrondiConfigData.configSampleObject,
            MigrondiConfigData.fsRelativeMigrondiConfigPath
          )

        let! result =
          File.ReadAllTextAsync(
            MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName
          )

        return result |> (serializer :> IMiConfigurationSerializer).Decode
      }

      let! fileResult =
        fileSystem.ReadConfigurationAsync(
          MigrondiConfigData.fsRelativeMigrondiConfigPath
        )

      Assert.AreEqual(expected, fileResult)
    }
    :> Task

  [<TestMethod>]
  member _.``Can write a migration file``() =
    let migrationsDirPath =
      Path.Combine(rootDir.FullName, MigrationData.directoryName)

    let migrations = MigrationData.getMigrationObjects 3


    let encoded =
      migrations
      |> List.map(fun (migration, name) ->
        let encoded =
          (serializer :> IMiMigrationSerializer).EncodeText migration

        let name = Path.GetFileName(name)
        name, encoded
      )
      |> Map.ofList

    // write them to disk
    migrations
    |> List.iter(fun (migration, name) ->
      fileSystem.WriteMigration(migration, name)
    )

    let files =
      Directory.GetFiles migrationsDirPath
      |> Array.Parallel.map(fun file ->
        (Path.GetFileName file), (File.ReadAllText file)
      )
      |> Array.toList

    let validations =
      files
      |> List.traverseResultA(fun (name, actual) ->
        match encoded |> Map.tryFind name with
        | Some expected -> Ok(expected, actual)
        | None -> Error $"Could not find file: '{name}' in expected files map"
      )

    match validations with
    | Ok validations ->
      validations
      |> List.iter(fun (expected, actual) ->
        Assert.AreEqual<string>(expected, actual)
      )
    | Error errs ->
      let errors = String.Join('\n', errs)
      Assert.Fail("Could not validate files:\n" + errors)


  [<TestMethod>]
  member _.``Can write a migration file async``() =
    task {
      let migrationsDirPath =
        Path.Combine(rootDir.FullName, MigrationData.directoryName)

      let migrations = MigrationData.getMigrationObjects 3

      let encoded =
        migrations
        |> List.map(fun (migration, name) ->
          let encoded =
            (serializer :> IMiMigrationSerializer).EncodeText migration

          let name = Path.GetFileName(name)
          name, encoded
        )
        |> Map.ofList

      // write them to disk
      for migration, name in migrations do
        do! fileSystem.WriteMigrationAsync(migration, name)

      let! files =
        Directory.GetFiles migrationsDirPath
        |> Array.Parallel.map(fun filepath -> async {
          let filename = Path.GetFileName filepath
          let! content = File.ReadAllTextAsync filepath |> Async.AwaitTask
          return filename, content
        })
        |> Async.Parallel

      let! validations =
        files
        |> List.ofArray
        |> List.traverseAsyncResultA(fun (name, actual) -> asyncResult {
          match encoded |> Map.tryFind name with
          | Some expected -> return expected, actual
          | None ->
            return!
              Error $"Could not find file: '{name}' in expected files map"
        })

      match validations with
      | Ok validations ->
        validations
        |> List.iter(fun (expected, actual) ->
          Assert.AreEqual<string>(expected, actual)
        )
      | Error errs ->
        let errors = String.Join('\n', errs)
        Assert.Fail("Could not validate files:\n" + errors)
    }
    :> Task

  [<TestMethod>]
  member _.``Can read migration``() =

    let migrations = MigrationData.getMigrationObjects 3

    let indexedMigration =
      migrations
      |> List.map(fun (migration, _) -> migration.name, migration)
      |> Map.ofList

    // write them to disk
    migrations
    |> List.iter(fun (migration, name) ->
      fileSystem.WriteMigration(migration, name)
    )

    let foundMigrations =
      migrations
      |> List.traverseResultA(fun (_, relativePath) ->
        try
          fileSystem.ReadMigration(relativePath) |> Ok
        with
        | :? SourceNotFound as e ->
          Error($"File '{e.name}' not found at '{e.path}'")
        | :? MalformedSource as e ->
          Error($"File '{e.SourceName}' is malformed: {e.Reason}\n{e.Content}")
      )

    match foundMigrations with
    | Ok actual ->
      actual
      |> List.iter(fun actual ->
        let expected = indexedMigration |> Map.find actual.name
        Assert.AreEqual(expected, actual)
      )
    | Error error ->
      let error = String.Join('\n', error)
      Assert.Fail($"Could not read migrations: {error}")

  [<TestMethod>]
  member _.``Can read migration async``() =
    task {

      let migrations = MigrationData.getMigrationObjects 3

      let indexedMigration =
        migrations
        |> List.map(fun (migration, _) -> migration.name, migration)
        |> Map.ofList

      // write them to disk
      for migration, name in migrations do
        do! fileSystem.WriteMigrationAsync(migration, name)


      let! foundMigrations =
        migrations
        |> List.traverseAsyncResultA(fun (_, relativePath) -> asyncResult {

          let! migration = task {
            try
              let! value = fileSystem.ReadMigrationAsync(relativePath)
              return Ok value
            with
            | :? SourceNotFound as e ->
              return Error($"File '{e.name}' not found at '{e.path}'")
            | :? MalformedSource as e ->
              return
                Error(
                  $"File '{e.SourceName}' is malformed: {e.Reason}\n{e.Content}"
                )
          }

          return migration
        })

      match foundMigrations with
      | Ok actual ->
        actual
        |> List.iter(fun actual ->
          let expected = indexedMigration |> Map.find actual.name
          Assert.AreEqual(expected, actual)
        )
      | Error error ->
        let error = String.Join('\n', error)
        Assert.Fail($"Could not read migrations: {error}")
    }
    :> Task

  [<TestMethod>]
  member _.``Can list migrations in a directory``() =
    let migrationsDirPath =
      Path.Combine(rootDir.FullName, MigrationData.directoryName)

    let migrations = MigrationData.getMigrationObjects 3

    let indexedMigrations =
      migrations |> List.map(fun (m, _) -> m.name, m) |> Map.ofList

    // write them to disk
    migrations
    |> List.iter(fun (migration, name) ->
      fileSystem.WriteMigration(migration, name)
    )

    let foundMigrations =
      try
        fileSystem.ListMigrations(migrationsDirPath) |> List.ofSeq |> Ok
      with :? AggregateException as e ->
        e.InnerExceptions
        |> Seq.filter(fun err ->
          if err.GetType() = typeof<MalformedSource> then
            true
          else
            false
        )
        |> List.ofSeq
        |> List.traverseResultA(fun err ->
          let err = err :?> MalformedSource

          Error
            $"File '{err.SourceName}' is malformed: {err.Reason}\n{err.Content}"
        )


    match foundMigrations with
    | Ok actual ->
      actual
      |> List.iter(fun actual ->
        let expected = indexedMigrations |> Map.find actual.name
        Assert.AreEqual(expected, actual)
      )
    | Error error ->
      let error = String.Join('\n', error)
      Assert.Fail($"Could not read migrations: {error}")

  [<TestMethod>]
  member _.``Can list migrations in a directory async``() =
    let migrationsDirPath =
      Path.Combine(rootDir.FullName, MigrationData.directoryName)

    let migrations = MigrationData.getMigrationObjects 3

    let indexedMigrations =
      migrations |> List.map(fun (m, _) -> m.name, m) |> Map.ofList

    // write them to disk
    migrations
    |> List.iter(fun (migration, name) ->
      fileSystem.WriteMigration(migration, name)
    )

    let foundMigrations =
      try
        fileSystem.ListMigrations(migrationsDirPath) |> List.ofSeq |> Ok
      with :? AggregateException as e ->
        e.InnerExceptions
        |> Seq.filter(fun err ->
          if err.GetType() = typeof<MalformedSource> then
            true
          else
            false
        )
        |> List.ofSeq
        |> List.traverseResultA(fun err ->
          let err = err :?> MalformedSource

          Error
            $"File '{err.SourceName}' is malformed: {err.Reason}\n{err.Content}"
        )

    match foundMigrations with
    | Ok actual ->
      actual
      |> List.iter(fun actual ->
        let expected = indexedMigrations |> Map.find actual.name
        Assert.AreEqual(expected, actual)
      )
    | Error error ->
      let error = String.Join('\n', error)
      Assert.Fail($"Could not read migrations: {error}")

  [<TestMethod>]
  member _.``Can list migrations in a directory with mixed files``() =
    let migrationsDirPath =
      Path.Combine(rootDir.FullName, MigrationData.directoryName)

    // ensure it exists first
    Directory.CreateDirectory(migrationsDirPath) |> ignore

    let migrations = MigrationData.getMigrationObjects 3

    // write some random files to the directory
    File.WriteAllText(
      Path.Combine(migrationsDirPath, Path.GetRandomFileName()),
      "random file, this should not be taken into account"
    )

    File.WriteAllText(
      Path.Combine(migrationsDirPath, Path.GetRandomFileName()),
      "random file, this should not be taken into account"
    )

    let indexedMigrations =
      migrations |> List.map(fun (m, _) -> m.name, m) |> Map.ofList

    // write them to disk
    migrations
    |> List.iter(fun (migration, name) ->
      fileSystem.WriteMigration(migration, name)
    )

    let foundMigrations =
      try
        fileSystem.ListMigrations(migrationsDirPath) |> List.ofSeq |> Ok
      with :? AggregateException as e ->
        e.InnerExceptions
        |> Seq.filter(fun err ->
          if err.GetType() = typeof<MalformedSource> then
            true
          else
            false
        )
        |> List.ofSeq
        |> List.traverseResultA(fun err ->
          let err = err :?> MalformedSource

          Error
            $"File '{err.SourceName}' is malformed: {err.Reason}\n{err.Content}"
        )

    match foundMigrations with
    | Ok actual ->
      actual
      |> List.iter(fun actual ->
        let expected = indexedMigrations |> Map.find actual.name
        Assert.AreEqual(expected, actual)
      )
    | Error error ->
      let error = String.Join('\n', error)
      Assert.Fail($"Could not read migrations: {error}")