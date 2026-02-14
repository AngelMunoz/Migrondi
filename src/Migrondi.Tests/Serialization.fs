module Migrondi.Tests.Serialization

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
    """{"name":"AddUsersTable","timestamp":"1586550686936","upContent":"-- Write your Up migrations here\nCREATE TABLE IF NOT EXISTS migration(\n    id INTEGER PRIMARY KEY AUTOINCREMENT,\n    name VARCHAR(255) NOT NULL,\n    timestamp BIGINT NOT NULL\n);","downContent":"-- Write how to revert the migration here\nDROP TABLE IF EXISTS migrations;","manualTransaction":false}"""

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
  let textMigrationSampleV1ManualTransaction =
    """-- MIGRONDI:NAME=AddUsersTable
-- MIGRONDI:TIMESTAMP=1586550686936
-- MIGRONDI:ManualTransaction=True
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
    manualTransaction = false
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

  let serializer = MigrondiSerializer()
  let configSerializer = serializer :> IMiConfigurationSerializer
  let migrationSerializer = serializer :> IMiMigrationSerializer

  [<TestMethod>]
  member _.``Can Encode Configuration``() =
    let encoded = configSerializer.Encode ConfigurationData.configJsonObject

    Assert.AreEqual(
      ConfigurationData.configJsonSample,
      encoded,
      ignoreCase = true
    )

  [<TestMethod>]
  member _.``Can Decode Configuration``() =
    let decoded = configSerializer.Decode(ConfigurationData.configJsonSample)

    Assert.AreEqual(
      ConfigurationData.configJsonObject,
      decoded,
      "Configuration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Encode Json Migration``() =
    let encoded = migrationSerializer.EncodeJson(MigrationData.migrationObject)

    Assert.AreEqual<string>(MigrationData.jsonMigrationSample, encoded)

  [<TestMethod>]
  member _.``Can Encode Text Migration v1 with Manual Transaction``() =
    let actual =
      migrationSerializer.EncodeText(
        {
          MigrationData.migrationObject with
              manualTransaction = true
        }
      )

    let expected = MigrationData.textMigrationSampleV1ManualTransaction
    Assert.AreEqual<string>(expected, actual)

  [<TestMethod>]
  member _.``Can Decode Text Migration v0``() =
    let decoded =
      migrationSerializer.DecodeText(
        MigrationData.textMigrationSampleV0,
        MigrationData.migrationObject.name
      )

    Assert.AreEqual(
      MigrationData.migrationObject,
      decoded,
      "Migration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Decode Text Migration v1``() =
    let decoded =
      migrationSerializer.DecodeText(MigrationData.textMigrationSampleV1)

    Assert.AreEqual(
      MigrationData.migrationObject,
      decoded,
      "Migration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Decode Json MigrationRecord``() =
    let decoded =
      migrationSerializer.DecodeMigrationRecord(
        MigrationRecordData.migrationRecordJsonSample
      )

    Assert.AreEqual(
      MigrationRecordData.migrationRecordObject,
      decoded,
      "Migration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Encode Json MigrationRecord``() =
    let encoded =
      migrationSerializer.EncodeMigrationRecord(
        MigrationRecordData.migrationRecordObject
      )

    Assert.AreEqual<string>(
      MigrationRecordData.migrationRecordJsonSample,
      encoded
    )

  [<TestMethod>]
  member _.``Can Decode Text Migration v1 with Manual Transaction``() =
    let decoded =
      migrationSerializer.DecodeText(
        MigrationData.textMigrationSampleV1ManualTransaction
      )

    let expected = {
      MigrationData.migrationObject with
          manualTransaction = true
    }

    Assert.AreEqual(expected, decoded)

[<TestClass>]
type MiSerializerTests() =

  [<TestMethod>]
  member _.``Can Encode Migration to Text``() =
    let encoded = MiSerializer.Encode(MigrationData.migrationObject)

    Assert.AreEqual<string>(MigrationData.textMigrationSampleV1, encoded)

  [<TestMethod>]
  member _.``Can Decode Migration from Text``() =
    let decoded = MiSerializer.Decode(MigrationData.textMigrationSampleV1)

    Assert.AreEqual(
      MigrationData.migrationObject,
      decoded,
      "Migration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Decode Migration v0 with Name``() =
    let decoded =
      MiSerializer.Decode(
        MigrationData.textMigrationSampleV0,
        MigrationData.migrationObject.name
      )

    Assert.AreEqual(
      MigrationData.migrationObject,
      decoded,
      "Migration v0 should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Encode Config to JSON``() =
    let encoded = MiSerializer.Encode(ConfigurationData.configJsonObject)

    Assert.AreEqual(
      ConfigurationData.configJsonSample,
      encoded,
      ignoreCase = true
    )

  [<TestMethod>]
  member _.``Can Decode Config from JSON``() =
    let decoded = MiSerializer.DecodeConfig(ConfigurationData.configJsonSample)

    Assert.AreEqual(
      ConfigurationData.configJsonObject,
      decoded,
      "Configuration should be decoded correctly"
    )

  [<TestMethod>]
  member _.``Can Encode MigrationRecord to JSON``() =
    let encoded = MiSerializer.Encode(MigrationRecordData.migrationRecordObject)

    Assert.AreEqual<string>(
      MigrationRecordData.migrationRecordJsonSample,
      encoded
    )

  [<TestMethod>]
  member _.``Can Decode MigrationRecord from JSON``() =
    let decoded =
      MiSerializer.DecodeRecord(MigrationRecordData.migrationRecordJsonSample)

    Assert.AreEqual(
      MigrationRecordData.migrationRecordObject,
      decoded,
      "MigrationRecord should be decoded correctly"
    )
