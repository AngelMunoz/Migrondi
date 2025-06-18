module ProjectDetailsTests

open Xunit
open Avalonia.Headless.XUnit
open MigrondiUI.Components.ProjectDetails
open Migrondi.Core
open System
open Avalonia.Controls

[<AvaloniaFact>]
let ``MigrationStatusView - Applied``() =
  let migration = {
    name = "test"
    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    upContent = "up"
    downContent = "down"
    manualTransaction = false
  }

  let status = Applied migration
  let view = MigrationStatusView(status)
  let expander = view.Content :?> Expander
  let header = expander.Header :?> StackPanel
  let nameTextBlock = header.Children[0] :?> TextBlock
  let statusTextBlock = header.Children[1] :?> TextBlock

  Assert.Equal("test", nameTextBlock.Text)
  Assert.Equal(" [Applied]", statusTextBlock.Text)

[<AvaloniaFact>]
let ``MigrationStatusView - Pending``() =
  let migration = {
    name = "test"
    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    upContent = "up"
    downContent = "down"
    manualTransaction = false
  }

  let status = Pending migration
  let view = MigrationStatusView(status)
  let expander = view.Content :?> Expander
  let header = expander.Header :?> StackPanel
  let nameTextBlock = header.Children[0] :?> TextBlock
  let statusTextBlock = header.Children[1] :?> TextBlock

  Assert.Equal("test", nameTextBlock.Text)
  Assert.Equal(" [Pending]", statusTextBlock.Text)

[<AvaloniaFact>]
let ``DryRunView - Up shows upContent``() =
  let migration = {
    name = "test"
    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    upContent = "up content"
    downContent = "down content"
    manualTransaction = false
  }

  let view = DryRunView(Up, migration)
  let textEditor = view.Content :?> AvaloniaEdit.TextEditor
  Assert.Contains("up content", textEditor.Text)

[<AvaloniaFact>]
let ``DryRunView - Down shows downContent``() =
  let migration = {
    name = "test"
    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    upContent = "up content"
    downContent = "down content"
    manualTransaction = false
  }

  let view = DryRunView(Down, migration)
  let textEditor = view.Content :?> AvaloniaEdit.TextEditor
  Assert.Contains("down content", textEditor.Text)

[<AvaloniaFact>]
let ``DryRunView - wraps with transaction when manualTransaction is false``() =
  let migration = {
    name = "test"
    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    upContent = "up content"
    downContent = "down content"
    manualTransaction = false
  }

  let view = DryRunView(Up, migration)
  let textEditor = view.Content :?> AvaloniaEdit.TextEditor
  Assert.StartsWith("-- ----------START TRANSACTION----------", textEditor.Text)

  Assert.EndsWith(
    "-- ----------COMMIT TRANSACTION----------",
    textEditor.Text.TrimEnd([| '\n'; '\r' |])
  )

[<AvaloniaFact>]
let ``DryRunView - does not wrap with transaction when manualTransaction is true``
  ()
  =
  let migration = {
    name = "test"
    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    upContent = "up content"
    downContent = "down content"
    manualTransaction = true
  }

  let view = DryRunView(Up, migration)
  let textEditor = view.Content :?> AvaloniaEdit.TextEditor
  Assert.Equal("up content", textEditor.Text)

[<AvaloniaFact>]
let ``migrationListView - shows message when no migrations``() =
  let view = migrationListView [||]
  let textBlock = view :?> TextBlock
  Assert.Equal("No migrations found.", textBlock.Text)

[<AvaloniaFact>]
let ``migrationListView - shows migration views for each migration``() =
  let migration1 = {
    name = "test1"
    timestamp = 0L
    upContent = "up1"
    downContent = "down1"
    manualTransaction = false
  }

  let migration2 = {
    name = "test2"
    timestamp = 1L
    upContent = "up2"
    downContent = "down2"
    manualTransaction = false
  }

  let statuses = [| Applied migration1; Pending migration2 |]
  let view = migrationListView statuses
  let itemsControl = view :?> ItemsControl

  let items = [|
    for item in itemsControl.ItemsSource -> item :?> MigrationStatus
  |]

  Assert.Equal<MigrationStatus[]>(statuses, items)

[<AvaloniaFact>]
let ``dryRunListView - shows message when no migrations``() =
  let view = dryRunListView(DryRun DryUp, [||])
  let textBlock = view :?> TextBlock
  Assert.Equal("No dry run found.", textBlock.Text)

[<AvaloniaFact>]
let ``dryRunListView - shows message when not in dry run state``() =
  let migration1 = {
    name = "test1"
    timestamp = 0L
    upContent = "up1"
    downContent = "down1"
    manualTransaction = false
  }

  let view = dryRunListView(Migrations, [| migration1 |])
  let textBlock = view :?> TextBlock
  Assert.Equal("No dry run found.", textBlock.Text)

[<AvaloniaFact>]
let ``dryRunListView - shows dry run views for each migration``() =
  let migration1 = {
    name = "test1"
    timestamp = 0L
    upContent = "up1"
    downContent = "down1"
    manualTransaction = false
  }

  let migration2 = {
    name = "test2"
    timestamp = 1L
    upContent = "up2"
    downContent = "down2"
    manualTransaction = false
  }

  let migrations = [| migration1; migration2 |]
  let view = dryRunListView(DryRun DryUp, migrations)
  let stackPanel = view :?> StackPanel
  let header = stackPanel.Children[0] :?> TextBlock
  Assert.Contains("simulation Apply Migrations", header.Text)
  let itemsControl = stackPanel.Children[1] :?> ItemsControl

  Assert.Equal(
    2,
    (itemsControl.ItemsSource :?> (Migration * RunMigrationKind)[]).Length
  )

  let footer = stackPanel.Children[2] :?> TextBlock

  Assert.Equal(
    "Simulation completed. No changes were made to the database.",
    footer.Text
  )

[<AvaloniaFact>]
let ``MigrondiExceptionView - displays generic exception``() =
  let ex = Exception("generic error")
  let view = MigrondiExceptionView(ex)
  let stackPanel = view.Content :?> StackPanel
  let expander = stackPanel.Children[2] :?> Expander
  let errorTextBlock = expander.Content :?> TextBlock
  Assert.Equal("generic error", errorTextBlock.Text)
  let details = stackPanel.Children[1] :?> TextBlock
  Assert.Equal("No Error details available.", details.Text)

[<AvaloniaFact>]
let ``MigrondiExceptionView - displays SourceNotFound``() =
  let ex = SourceNotFound("path/to/file", "file.sql")
  let view = MigrondiExceptionView(ex)
  let stackPanel = view.Content :?> StackPanel
  let expander = stackPanel.Children[2] :?> Expander
  let errorTextBlock = expander.Content :?> TextBlock
  Assert.Equal(ex.Message, errorTextBlock.Text)
  let details = stackPanel.Children[1] :?> StackPanel
  Assert.Equal(2, details.Children.Count)

[<AvaloniaFact>]
let ``MigrondiExceptionView - displays MalformedSource``() =
  let ex = MalformedSource("bad content", "bad reason", "source.sql")
  let view = MigrondiExceptionView(ex)
  let stackPanel = view.Content :?> StackPanel
  let expander = stackPanel.Children[2] :?> Expander
  let errorTextBlock = expander.Content :?> TextBlock
  Assert.Equal(ex.Message, errorTextBlock.Text)
  let details = stackPanel.Children[1] :?> StackPanel
  Assert.Equal(3, details.Children.Count)
