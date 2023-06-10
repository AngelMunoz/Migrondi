module Migrondi.Tests.Serialization

open System
open Microsoft.VisualStudio.TestTools.UnitTesting

open Migrondi.Core
open Migrondi.Core.Serialization


module ConfigurationData =
  [<Literal>]
  let configJsonSample =
    """{
  "connection": "connection",
  "migrations": "./migrations",
  "tableName": "migrations",
  "driver": "sqlite"
}"""

  let configJsonObject = {
    connection = "connection"
    migrations = "./migrations"
    tableName = "migrations"
    driver = MigrondiDriver.Sqlite
  }

module MigrationData =
  [<Literal>]
  let jsonMigrationSample =
    """{"name":"AddUsersTable","timestamp":"1586550686936","upContent":"-- Write your Up migrations here\nCREATE TABLE IF NOT EXISTS migration(\n    id INTEGER PRIMARY KEY AUTOINCREMENT,\n    name VARCHAR(255) NOT NULL,\n    timestamp BIGINT NOT NULL\n);","downContent":"-- Write how to revert the migration here\nDROP TABLE IF EXISTS migrations;"}"""

  [<Literal>]
  let textMigrationSampleV1 =
    """-- MIGRONDI:NAME=AddUsersTable
-- MIGRONDI:TIMESTAMP=1586550686936
-- ---------- MIGRONDI:UP ----------
-- Write your Up migrations here
CREATE TABLE IF NOT EXISTS migration(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name VARCHAR(255) NOT NULL,
    timestamp BIGINT NOT NULL
);
-- ---------- MIGRONDI:DOWN ----------
-- Write how to revert the migration here
DROP TABLE IF EXISTS migrations;
"""

  [<Literal>]
  let textMigrationSampleV0 =
    """-- ---------- MIGRONDI:UP:1586550686936 --------------
-- Write your Up migrations here
CREATE TABLE IF NOT EXISTS migration(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name VARCHAR(255) NOT NULL,
    timestamp BIGINT NOT NULL
);
-- ---------- MIGRONDI:DOWN:1586550686936 --------------
-- Write how to revert the migration here
DROP TABLE IF EXISTS migrations;
"""

  let migrationObject = {
    name = "AddUsersTable"
    timestamp = 1586550686936L
    upContent =
      """-- Write your Up migrations here
CREATE TABLE IF NOT EXISTS migration(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name VARCHAR(255) NOT NULL,
    timestamp BIGINT NOT NULL
);"""
    downContent =
      """-- Write how to revert the migration here
DROP TABLE IF EXISTS migrations;"""
  }

module MigrationRecordData =

  [<Literal>]
  let migrationRecordJsonSample =
    """{"id":"1","name":"AddUsersTable","timestamp":"1586550686936"}"""

  let migrationRecordObject = {
    id = 1L
    name = "AddUsersTable"
    timestamp = 1586550686936L
  }


[<TestClass>]
type SerializationTests() =
  let serializer = SerializerImpl.BuildDefaultEnv()

  [<TestMethod>]
  member _.``Can Encode Configuration``() =
    let encoded =
      serializer.ConfigurationSerializer.Encode
        ConfigurationData.configJsonObject

    Assert.AreEqual(
      ConfigurationData.configJsonSample,
      encoded,
      ignoreCase = true
    )

  [<TestMethod>]
  member _.``Can Decode Configuration``() =
    let decoded =
      match
        serializer.ConfigurationSerializer.Decode(
          ConfigurationData.configJsonSample
        )
      with
      | Ok x -> x
      | Error e -> failwith $"Decoding Failed: {e}"

    Assert.AreEqual(
      ConfigurationData.configJsonObject,
      decoded,
      "Configuration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Encode Json Migration``() =
    let encoded =
      serializer.MigrationSerializer.EncodeJson(MigrationData.migrationObject)

    Assert.AreEqual(
      MigrationData.jsonMigrationSample,
      encoded,
      "Migration should be encoded correctly"
    )

  [<TestMethod>]
  member _.``Can Encode Text Migration v1``() =
    let actual =
      serializer.MigrationSerializer.EncodeText(MigrationData.migrationObject)

    let expected = MigrationData.textMigrationSampleV1
    Assert.AreEqual(
      expected,
      actual
    )

  [<TestMethod>]
  member _.``Can Decode Text Migration v0``() =
    let decoded =
      match
        serializer.MigrationSerializer.DecodeText(
          MigrationData.textMigrationSampleV0,
          MigrationData.migrationObject.name
        )
      with
      | Ok x -> x
      | Error e -> failwith $"Decoding Failed: {e}"

    Assert.AreEqual(
      MigrationData.migrationObject,
      decoded,
      "Migration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Decode Text Migration v1``() =
    let decoded =
      match
        serializer.MigrationSerializer.DecodeText(
          MigrationData.textMigrationSampleV1
        )
      with
      | Ok x -> x
      | Error e -> failwith $"Decoding Failed: {e}"

    Assert.AreEqual(
      MigrationData.migrationObject,
      decoded,
      "Migration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Decode Json MigrationRecord``() =
    let decoded =
      match
        serializer.MigrationRecordSerializer.Decode(
          MigrationRecordData.migrationRecordJsonSample
        )
      with
      | Ok x -> x
      | Error e -> failwith $"Decoding Failed: {e}"

    Assert.AreEqual(
      MigrationRecordData.migrationRecordObject,
      decoded,
      "Migration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Encode Json MigrationRecord``() =
    let encoded =
      serializer.MigrationRecordSerializer.Encode(
        MigrationRecordData.migrationRecordObject
      )

    Assert.AreEqual(
      MigrationRecordData.migrationRecordJsonSample,
      encoded,
      "Migration should be encoded correctly"
    )