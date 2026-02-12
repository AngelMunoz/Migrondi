module MigrondiUI.Tests.TestHelpers

open System
open Migrondi.Core

/// Creates a test migration with the given name and default values
let createTestMigration name : Migration = {
  name = name
  timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
  upContent = "CREATE TABLE test (id INTEGER PRIMARY KEY);"
  downContent = "DROP TABLE test;"
  manualTransaction = false
}
