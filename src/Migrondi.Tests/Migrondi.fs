namespace Migrondi.Tests.Migrondi

open System
open System.Threading.Tasks

open Microsoft.Extensions.Logging
open Microsoft.VisualStudio.TestTools.UnitTesting

open Migrondi.Core
open Migrondi.Core.FileSystem

[<TestClass>]
type MigrondiUtils() =
  [<TestMethod>]
  member _.``getConnectionStr returns original connection string if path is rooted``
    ()
    =
    let rootPath = "/root/project"

    let config = {
      driver = MigrondiDriver.Sqlite
      connection = "Data Source=/absolute/path/to/db.sqlite;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let result = MigrondiserviceImpl.getConnectionStr rootPath config

    Assert.AreEqual<string>(config.connection, result)

  [<TestMethod>]
  member _.``getConnectionStr prepends rootPath if path is not rooted``() =
    let rootPath = "/root/project"

    let config = {
      driver = MigrondiDriver.Sqlite
      connection = "Data Source=relative/path/to/db.sqlite;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let expected =
      "Data Source="
      + System.IO.Path.Combine(rootPath, "relative/path/to/db.sqlite;")

    let result = MigrondiserviceImpl.getConnectionStr rootPath config

    Assert.IsTrue(result.StartsWith("Data Source="))

    Assert.IsTrue(
      result.Contains(
        System.IO.Path.Combine(rootPath, "relative/path/to/db.sqlite")
      )
    )

  [<TestMethod>]
  member _.``getConnectionStr returns original connection for non-Sqlite drivers``
    ()
    =
    let rootPath = "/root/project"

    let config = {
      driver = MigrondiDriver.Mssql
      connection = "Server=localhost;Database=TestDb;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let result = MigrondiserviceImpl.getConnectionStr rootPath config

    Assert.AreEqual<string>(config.connection, result)

  [<TestMethod>]
  member _.``getConnectionStr handles ./ relative path correctly``() =
    let rootPath = "/root/project"

    let config = {
      driver = MigrondiDriver.Sqlite
      connection = "Data Source=./db.sqlite;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let result = MigrondiserviceImpl.getConnectionStr rootPath config

    Assert.IsTrue(
      result.Contains(System.IO.Path.Combine(rootPath, "./db.sqlite"))
    )

  [<TestMethod>]
  member _.``getConnectionStr handles ../ relative path correctly``() =
    let rootPath = "/root/project"

    let config = {
      driver = MigrondiDriver.Sqlite
      connection = "Data Source=../db.sqlite;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let result = MigrondiserviceImpl.getConnectionStr rootPath config

    Assert.IsTrue(
      result.Contains(System.IO.Path.Combine(rootPath, "../db.sqlite"))
    )

  [<TestMethod>]
  member _.``getConnectionStr returns original connection string if path is rooted Windows path``
    ()
    =
    let rootPath = "/root/project"

    let config = {
      driver = MigrondiDriver.Sqlite
      connection = "Data Source=C:\\absolute\\windows\\db.sqlite;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let result = MigrondiserviceImpl.getConnectionStr rootPath config

    Assert.AreEqual<string>(config.connection, result)

[<TestClass>]
type MigrondiFactoryTests() =

  let loggerFactory =
    LoggerFactory.Create(fun builder ->
      builder.SetMinimumLevel(LogLevel.Debug).AddSimpleConsole() |> ignore)

  let logger = loggerFactory.CreateLogger("Migrondi:Tests.MigrondiFactory")

  [<TestMethod>]
  member _.``MigrondiFactory resolves local relative paths correctly``() =
    let relativePath = "."

    let config = {
      driver = MigrondiDriver.Sqlite
      connection = "Data Source=:memory:;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let migrondi =
      Migrondi.MigrondiFactory(config, relativePath, logger = logger)

    Assert.IsNotNull(migrondi)

  [<TestMethod>]
  member _.``MigrondiFactory resolves local absolute paths correctly``() =
    let tempPath = IO.Path.GetTempPath()

    let config = {
      driver = MigrondiDriver.Sqlite
      connection = "Data Source=:memory:;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let migrondi = Migrondi.MigrondiFactory(config, tempPath, logger = logger)

    Assert.IsNotNull(migrondi)

  [<TestMethod>]
  member _.``MigrondiFactory preserves virtual URI scheme migrondi-ui``() =
    let mutable receivedUri: Uri option = None

    let mockSource =
      { new IMiMigrationSource with
          member _.ReadContent(uri: Uri) =
            receivedUri <- Some uri
            ""

          member _.ReadContentAsync(uri: Uri, ?cancellationToken) =
            receivedUri <- Some uri
            Task.FromResult("")

          member _.WriteContent(uri: Uri, content: string) =
            receivedUri <- Some uri

          member _.WriteContentAsync
            (
              uri: Uri,
              content: string,
              ?cancellationToken: Threading.CancellationToken
            ) =
            receivedUri <- Some uri
            Task.CompletedTask

          member _.ListFiles(locationUri: Uri) =
            receivedUri <- Some locationUri
            Seq.empty

          member _.ListFilesAsync
            (locationUri: Uri, ?cancellationToken: Threading.CancellationToken)
            =
            receivedUri <- Some locationUri
            Task.FromResult(Seq.empty :> Uri seq)
      }

    let rootDir = "migrondi-ui://projects/virtual/test-123/"

    let config = {
      driver = MigrondiDriver.Mssql
      connection = "Server=localhost;Database=TestDb;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let migrondi =
      Migrondi.MigrondiFactory(
        config,
        rootDir,
        logger = logger,
        migrationSource = mockSource
      )

    try
      migrondi.MigrationsList() |> ignore
    with _ ->
      ()

    match receivedUri with
    | Some uri ->
      Assert.IsTrue(
        uri.Scheme = "migrondi-ui",
        $"Expected scheme 'migrondi-ui' but got '{uri.Scheme}'"
      )

      Assert.IsFalse(
        uri.ToString().Contains("file:///"),
        $"URI should not contain 'file:///' but got '{uri}'"
      )
    | None -> Assert.Fail("URI was never passed to source")

  [<TestMethod>]
  member _.``MigrondiFactory preserves https URI scheme``() =
    let mutable receivedUri: Uri option = None

    let mockSource =
      { new IMiMigrationSource with
          member _.ReadContent(uri: Uri) =
            receivedUri <- Some uri
            ""

          member _.ReadContentAsync(uri: Uri, ?cancellationToken) =
            receivedUri <- Some uri
            Task.FromResult("")

          member _.WriteContent(uri: Uri, content: string) =
            receivedUri <- Some uri

          member _.WriteContentAsync
            (
              uri: Uri,
              content: string,
              ?cancellationToken: Threading.CancellationToken
            ) =
            receivedUri <- Some uri
            Task.CompletedTask

          member _.ListFiles(locationUri: Uri) =
            receivedUri <- Some locationUri
            Seq.empty

          member _.ListFilesAsync
            (locationUri: Uri, ?cancellationToken: Threading.CancellationToken)
            =
            receivedUri <- Some locationUri
            Task.FromResult(Seq.empty :> Uri seq)
      }

    let rootDir = "https://example.com/migrations/"

    let config = {
      driver = MigrondiDriver.Mssql
      connection = "Server=localhost;Database=TestDb;"
      migrations = "migrations"
      tableName = "Migrations"
    }

    let migrondi =
      Migrondi.MigrondiFactory(
        config,
        rootDir,
        logger = logger,
        migrationSource = mockSource
      )

    try
      migrondi.MigrationsList() |> ignore
    with _ ->
      ()

    match receivedUri with
    | Some uri ->
      Assert.IsTrue(
        uri.Scheme = "https",
        $"Expected scheme 'https' but got '{uri.Scheme}'"
      )

      Assert.IsTrue(
        uri.ToString().StartsWith("https://example.com/"),
        $"Expected URI to start with 'https://example.com/' but got '{uri}'"
      )
    | None -> Assert.Fail("URI was never passed to source")
