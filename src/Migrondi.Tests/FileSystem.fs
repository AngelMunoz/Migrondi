module Migrondi.Tests.FileSystem


open System
open System.IO
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting

open FSharp.UMX

open Migrondi.Core
open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem
open Migrondi.Core.FileSystem.Units

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

  let getMigrationObjects(amount: int) =
    [
      for i in 1.. amount + 1 do
        // ensure the timestamps are different
        let timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1L
        {
          name = $"AddTable{i}"
          upContent = "CREATE TABLE IF NOT EXISTS migration(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name VARCHAR(255) NOT NULL,
  timestamp BIGINT NOT NULL
);"
          downContent = $"DROP TABLE migration;"
          timestamp = timestamp
        },
        Path.Combine(directoryName, $"AddTable{i}_{timestamp}.sql")
    ]


[<TestClass>]
type FileSystemTests() =

  let baseUri =
    let tmp = Path.GetTempPath()

    let path = $"{Path.Combine(tmp, Guid.NewGuid().ToString())}%c{Path.DirectorySeparatorChar}"

    Uri(path, UriKind.Absolute)

  let rootDir = DirectoryInfo(baseUri.LocalPath)

  let fileSystem = FileSystemImpl.BuildDefaultEnv(baseUri)
  let serializer = SerializerImpl.BuildDefaultEnv()

  do
    printfn $"Using '{rootDir.FullName}' as Root Directory"

  [<TestCleanup>]
  member _.TestCleanup() =
    // We're done with these tests remove any temporary files
    rootDir.Delete(true)
    printfn $"Deleted temporary root dir at: '{rootDir.FullName}'"


  [<TestMethod>]
  member _.``Can write a migrondi.json file``() =

    let expected =
      serializer.ConfigurationSerializer.Encode
        MigrondiConfigData.configSampleObject

    fileSystem.WriteConfiguration(
      serializer,
      MigrondiConfigData.configSampleObject,
      UMX.tag<RelativeUserPath> MigrondiConfigData.fsRelativeMigrondiConfigPath
    )

    let actual =
      let path = MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName
      File.ReadAllText path

    Assert.AreEqual(expected, actual)

  [<TestMethod>]
  member _.``Can read a migrondi.json file``() =
    let expected =

      fileSystem.WriteConfiguration(
        serializer,
        MigrondiConfigData.configSampleObject,
        UMX.tag<RelativeUserPath>
          MigrondiConfigData.fsRelativeMigrondiConfigPath
      )

      File.ReadAllText(MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName)
      |> serializer.ConfigurationSerializer.Decode
      |> Result.defaultWith(fun _ -> failwith "Could not decode config file")

    let fileResult =
      fileSystem.ReadConfiguration(
        serializer,
        UMX.tag<RelativeUserPath>
          MigrondiConfigData.fsRelativeMigrondiConfigPath
      )

    match fileResult with
    | Ok actual ->

      Assert.AreEqual(expected, actual)
    | Error(ReadFileError.FileNotFound(filepath, filename)) ->
      Assert.Fail($"File '{filename}' not found at '{filepath}'")
    | Error(ReadFileError.Malformedfile(filename, serializationError)) ->
      Assert.Fail(
        $"File '{filename}' is malformed: {serializationError.Reason}\n{serializationError.Content}"
      )

  [<TestMethod>]
  member _.``Can write a migrondi.json file async``() =
    task {
      let expected =
        serializer.ConfigurationSerializer.Encode
          MigrondiConfigData.configSampleObject

      do!
        fileSystem.WriteConfigurationAsync(
          serializer,
          MigrondiConfigData.configSampleObject,
          UMX.tag<RelativeUserPath>
            MigrondiConfigData.fsRelativeMigrondiConfigPath
        )

      let! actual =
        let path = MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName
        File.ReadAllTextAsync path

      Assert.AreEqual(expected, actual)
    }
    :> Task

  [<TestMethod>]
  member _.``Can read a migrondi.json file async``() =
    task {
      let! expected = task {
        do!
          fileSystem.WriteConfigurationAsync(
            serializer,
            MigrondiConfigData.configSampleObject,
            UMX.tag<RelativeUserPath>
              MigrondiConfigData.fsRelativeMigrondiConfigPath
          )

        let! result =
          File.ReadAllTextAsync(
            MigrondiConfigData.fsMigrondiConfigPath rootDir.FullName
          )

        return
          result
          |> serializer.ConfigurationSerializer.Decode
          |> Result.defaultWith(fun _ ->
            failwith "Could not decode config file"
          )
      }

      let! fileResult =
        fileSystem.ReadConfigurationAsync(
          serializer,
          UMX.tag<RelativeUserPath>
            MigrondiConfigData.fsRelativeMigrondiConfigPath
        )

      match fileResult with
      | Ok actual -> Assert.AreEqual(expected, actual)
      | Error(ReadFileError.FileNotFound(filepath, filename)) ->
        Assert.Fail($"File '{filename}' not found at '{filepath}'")
      | Error(ReadFileError.Malformedfile(filename, serializationError)) ->
        Assert.Fail(
          $"File '{filename}' is malformed: {serializationError.Reason}\n{serializationError.Content}"
        )
    }
    :> Task

  [<TestMethod>]
  member _.``Can write a migration file``() =
      let migrationsDirPath = Path.Combine(rootDir.FullName, MigrationData.directoryName)

      let migrations  =
        MigrationData.getMigrationObjects 3


      let encoded =
        migrations
        |> List.map (fun (migration, name) ->
          let encoded = serializer.MigrationSerializer.EncodeText migration
          let name = Path.GetFileName(name)
          name, encoded
        )
        |> Map.ofList

      // write them to disk
      migrations
      |> List.iter(fun (migration, name) ->
        fileSystem.WriteMigration(
          serializer,
          migration,
          UMX.tag<RelativeUserPath> name
        )
      )

      let files =
        Directory.GetFiles migrationsDirPath
        |> Array.Parallel.map(fun file -> (Path.GetFileName file), (File.ReadAllText file))
        |> Array.toList

      let validations =
        files |> List.traverseResultA(fun (name, actual)->
          match encoded |> Map.tryFind name with
          | Some expected ->
            Ok (expected, actual)
          | None ->
            Error $"Could not find file: '{name}' in expected files map"
        )

      match validations with
      | Ok validations ->
        validations |> List.iter Assert.AreEqual
      | Error errs ->
        let errors = String.Join('\n', errs)
        Assert.Fail("Could not validate files:\n" + errors)
