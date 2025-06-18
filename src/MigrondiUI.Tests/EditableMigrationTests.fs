module MigrondiUI.Tests.EditableMigrationTests

open System.Threading.Tasks
open Avalonia.Interactivity
open Xunit
open Avalonia.Controls
open Avalonia.Headless.XUnit
open Migrondi.Core
open MigrondiUI.Components.EditableMigration
open MigrondiUI.Tests.TestHelpers

[<AvaloniaFact>]
let ``EditableMigrationView - Status text updates on content change``() =
  // Arrange
  let migration = createTestMigration "test_migration"
  let migrationStatus = MigrationStatus.Pending migration

  // Act
  let view =
    EditableMigrationView(
      migrationStatus,
      (fun _ -> async { return true }),
      (fun () -> async { return () })
    )

  // Assert
  let expander = view.Content :?> Expander
  let header = expander.Header :?> StackPanel
  let statusTextBlock = header.Children[1] :?> TextBlock

  // Initial status should be just [Pending]
  Assert.Equal("[Pending]", statusTextBlock.Text)

  // Get the TextEditor and change content
  let content = expander.Content :?> StackPanel
  let migrationContentGrid = content.Children[1] :?> Grid
  let upFieldCol0 = migrationContentGrid.Children[0]

  let upEditorWrapper =
    (upFieldCol0 :?> Panel).Children[1] :?> AvaloniaEdit.TextEditor

  // Simulate changing the content
  upEditorWrapper.Text <- "CREATE TABLE modified (id INTEGER PRIMARY KEY);"

  // After content change, status should indicate unsaved changes
  Assert.Equal("[Pending] (Unsaved Changes)", statusTextBlock.Text)

[<AvaloniaFact>]
let ``EditableMigrationView - Shows manual transaction status correctly``() =
  // Arrange
  let migration = {
    createTestMigration "test_migration" with
        manualTransaction = true
  }

  let migrationStatus = Pending migration

  // Act
  let view =
    EditableMigrationView(
      migrationStatus,
      (fun _ -> async { return true }),
      (fun () -> async { return () })
    )

  // Assert
  let expander = view.Content :?> Expander
  let content = expander.Content :?> StackPanel
  let manualTransactionField = content.Children[0] :?> StackPanel
  let valueText = manualTransactionField.Children[1] :?> TextBlock

  // Verify the manual transaction field shows the correct value
  Assert.Equal("true", valueText.Text)

[<AvaloniaFact>]
let ``EditableMigrationView - Save with unsuccessful result doesn't update migration``
  ()
  =
  // Arrange
  let migration = createTestMigration "test_migration"
  let migrationStatus = Pending migration
  let mutable saveRequestedMigration = Unchecked.defaultof<Migration>

  // Act
  let view =
    EditableMigrationView(
      migrationStatus,
      (fun m ->
        saveRequestedMigration <- m
        async { return false }), // Return false to indicate save failed
      (fun () -> async { return () })
    )

  // Get editor and modify content
  let expander = view.Content :?> Expander
  let content = expander.Content :?> StackPanel
  let migrationContentGrid = content.Children[1] :?> Grid
  let upFieldCol0 = migrationContentGrid.Children[0]

  let upEditorWrapper =
    (upFieldCol0 :?> Panel).Children[1] :?> AvaloniaEdit.TextEditor

  // Simulate changing the content
  upEditorWrapper.Text <- "CREATE TABLE modified (id INTEGER PRIMARY KEY);"

  // Find and click save button
  let header = expander.Header :?> StackPanel
  let saveUserControl = header.Children[3] :?> UserControl
  let saveBtn = saveUserControl.Content :?> Button
  saveBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  // After unsuccessful save, status should still show unsaved changes
  let statusTextBlock = header.Children[1] :?> TextBlock
  Assert.Equal("[Pending] (Unsaved Changes)", statusTextBlock.Text)

[<AvaloniaFact>]
let ``saveMigrationBtn and removeMigrationBtn should disable during operation``
  ()
  =

  // Arrange - for save button
  let saveCompletionSource = TaskCompletionSource<unit>()

  let saveBtn =
    saveMigrationBtn(fun () -> async {
      do! Async.AwaitTask(saveCompletionSource.Task)
      return ()
    })

  // Act - verify initial state
  Assert.True(saveBtn.IsEnabled)

  // Click button to start async operation
  saveBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  // Assert - button should be disabled during operation
  Assert.False(saveBtn.IsEnabled)

  // Complete the operation
  saveCompletionSource.SetResult(())

  Avalonia.Threading.Dispatcher.UIThread.RunJobs()

  Assert.True(saveBtn.IsEnabled)

  // Arrange - for remove button
  let removeCompletionSource = TaskCompletionSource<unit>()

  let removeBtn =
    removeMigrationBtn(fun () -> async {
      do! Async.AwaitTask(removeCompletionSource.Task)
      return ()
    })

  // Act - verify initial state
  Assert.True(removeBtn.IsEnabled)

  // Click button to start async operation
  removeBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  // Assert - button should be disabled during operation
  Assert.False(removeBtn.IsEnabled)

  // Complete the operation
  removeCompletionSource.SetResult(())
  Avalonia.Threading.Dispatcher.UIThread.RunJobs()
  Assert.True(removeBtn.IsEnabled)

[<AvaloniaFact>]
let ``EditableMigrationView - Both up and down content changes should be saved``
  ()
  =
  // Arrange
  let migration = createTestMigration "test_migration"
  let migrationStatus = Pending migration
  let mutable savedMigration = Unchecked.defaultof<Migration>

  // Act
  let view =
    EditableMigrationView(
      migrationStatus,
      (fun m ->
        savedMigration <- m
        async { return true }),
      (fun () -> async { return () })
    )

  // Get up and down editors
  let expander = view.Content :?> Expander
  let content = expander.Content :?> StackPanel
  let migrationContentGrid = content.Children[1] :?> Grid

  let upFieldCol0 = migrationContentGrid.Children[0]

  let upEditorWrapper =
    (upFieldCol0 :?> Panel).Children[1] :?> AvaloniaEdit.TextEditor

  let downFieldCol2 = migrationContentGrid.Children[2]

  let downEditorWrapper =
    (downFieldCol2 :?> Panel).Children[1] :?> AvaloniaEdit.TextEditor

  // Modify both contents
  upEditorWrapper.Text <- "CREATE TABLE up_modified (id INTEGER PRIMARY KEY);"
  downEditorWrapper.Text <- "DROP TABLE up_modified;"

  // Find and click save button
  let header = expander.Header :?> StackPanel
  let saveUserControl = header.Children[3] :?> UserControl
  let saveBtn = saveUserControl.Content :?> Button
  saveBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  // Verify both contents were saved
  Assert.Equal(
    "CREATE TABLE up_modified (id INTEGER PRIMARY KEY);",
    savedMigration.upContent
  )

  Assert.Equal("DROP TABLE up_modified;", savedMigration.downContent)
