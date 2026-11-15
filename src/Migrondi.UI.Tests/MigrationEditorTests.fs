module Migrondi.UI.Tests.MigrationEditorTests

open Xunit

open Migrondi.Core
open Migrondi.UI.Views.MigrationEditor
open Migrondi.UI.Views.ProjectDetails

let sampleMigration: Migration = {
  name = "add_users"
  timestamp = 1586550686936L
  upContent = "CREATE TABLE users (id INTEGER PRIMARY KEY);"
  downContent = "DROP TABLE users;"
  manualTransaction = false
}

[<Fact>]
let ``isDirty is false for a fresh draft``() : unit =
  let state: EditorState = {
    Origin = sampleMigration
    Up = sampleMigration.upContent
    Down = sampleMigration.downContent
    ManualTransaction = sampleMigration.manualTransaction
    Busy = false
  }

  Assert.False(isDirty state)

[<Fact>]
let ``isDirty is true when any section differs``() : unit =
  let edited = {
    sampleMigration with
        upContent = "SELECT 1;"
  }

  let state: EditorState = {
    Origin = sampleMigration
    Up = edited.upContent
    Down = sampleMigration.downContent
    ManualTransaction = sampleMigration.manualTransaction
    Busy = false
  }

  Assert.True(isDirty state)

[<Fact>]
let ``editorKey is stable per migration``() : unit =
  let same = {
    sampleMigration with
        upContent = "SELECT 1;"
  }

  let other = {
    sampleMigration with
        timestamp = sampleMigration.timestamp + 1L
  }

  Assert.Equal(editorKey sampleMigration, editorKey same)
  Assert.NotEqual<string>(editorKey sampleMigration, editorKey other)

[<Fact>]
let ``dryContent wraps plain migrations in transaction markers``() : unit =
  let content = dryContent(true, sampleMigration)

  Assert.Contains("START TRANSACTION", content)
  Assert.Contains(sampleMigration.upContent, content)
  Assert.Contains("COMMIT TRANSACTION", content)

[<Fact>]
let ``dryContent leaves manual-transaction migrations alone``() : unit =
  let manual = {
    sampleMigration with
        manualTransaction = true
  }

  Assert.Equal(sampleMigration.downContent, dryContent(false, manual))
