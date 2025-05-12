namespace Migrondi.Tests.Migrondi

open Microsoft.VisualStudio.TestTools.UnitTesting

open Migrondi.Core

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