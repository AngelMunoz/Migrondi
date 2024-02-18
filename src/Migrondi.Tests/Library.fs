module Migrondi.Tests.Core

open System
open Microsoft.VisualStudio.TestTools.UnitTesting

open Migrondi.Core

[<TestClass>]
type CoreTests() =

  [<TestMethod>]
  [<DataRow("CreateUsersTable_1686453184242.sql",
            "CreateUsersTable",
            1686453184242L)>]
  [<DataRow("remove-students-name_1686366863404.sql",
            "remove-students-name",
            1686366863404L)>]
  [<DataRow("add products table_1686280663373.sql",
            "add products table",
            1686280663373L)>]
  member _.``Can extract name and timestamp from migration file name``
    (
      filename: string,
      expectedName: string,
      expectedTimestamp: int64
    ) =
    match Migration.ExtractFromFilename filename with
    | Ok(name, timestamp) ->
      Assert.AreEqual(expectedName, name)
      Assert.AreEqual(expectedTimestamp, timestamp)
    | Error error ->
      let error = String.Join(Environment.NewLine, error)
      Assert.Fail(error)

  [<TestMethod>]
  [<DataRow("/work/path/migrations/CreateUsersTable_1686453184242.sql",
            "CreateUsersTable",
            1686453184242L)>]
  [<DataRow("/work/path/migrations/remove-students-name_1686366863404.sql",
            "remove-students-name",
            1686366863404L)>]
  [<DataRow("/home/user/migrations/path/add products table_1686280663373.sql",
            "add products table",
            1686280663373L)>]
  member _.``Can extract name and timestamp from migration file path name``
    (
      filepath: string,
      expectedName: string,
      expectedTimestamp: int64
    ) =
    match Migration.ExtractFromPath filepath with
    | Ok(name, timestamp) ->
      Assert.AreEqual(expectedName, name)
      Assert.AreEqual(expectedTimestamp, timestamp)
    | Error error ->
      let error = String.Join(Environment.NewLine, error)
      Assert.Fail(error)