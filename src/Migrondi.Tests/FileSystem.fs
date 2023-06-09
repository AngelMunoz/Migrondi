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


module MigrondiConfigData =
  [<Literal>]
  let fsMigrondiPath = "fs-migrondi-config/migrondi.json"

  let fsMigrondiConfigPath (root: string) =
    Path.Combine(root, "fs-migrondi-config", "migrondi.json")

  let fsRelativeMigrondiConfigPath =
    Path.Combine("fs-migrondi-config", "migrondi.json")

  let configSampleObject = {
    connection = "connection"
    migrations = "./migrations"
    tableName = "migrations"
    driver = MigrondiDriver.Sqlite
  }


[<TestClass>]
type FileSystemTests() =

  let baseUri =
    let tmp = Path.GetTempPath()
    let path = $"{Path.Combine(tmp, Guid.NewGuid().ToString())}/"
    Uri(path, UriKind.Absolute)

  let rootDir = DirectoryInfo(baseUri.LocalPath)

  let fileSystem = FileSystemImpl.BuildDefaultEnv(baseUri)
  let serializer = SerializerImpl.BuildDefaultEnv()


  [<TestInitialize>]
  member _.InitializeTest() =
    rootDir.Create()
    rootDir.CreateSubdirectory("fs-migrondi-config") |> ignore
    rootDir.CreateSubdirectory("fs-migrations") |> ignore

    printfn $"Created temporary root dir at: '{rootDir.FullName}'"

  [<TestCleanup>]
  member _.CleanupTest() =
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