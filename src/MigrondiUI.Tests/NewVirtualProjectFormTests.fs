module NewVirtualProjectFormTests

open Xunit
open Avalonia.Headless.XUnit
open MigrondiUI.Components.NewVirtualProjectForm
open Migrondi.Core
open Avalonia.Controls
open MigrondiUI.Projects
open Avalonia.Interactivity

[<AvaloniaFact>]
let ``NewVirtualProjectForm - should call onCreateVirtualProject with correct arguments``
  ()
  =
  let mutable createdProject: NewVirtualProjectArgs option = None
  let onCreate(args: NewVirtualProjectArgs) = createdProject <- Some args
  let onImport() = ()

  let view = NewVirtualProjectForm(onCreate, onImport)
  let contentGrid = view.Content :?> Grid
  let formGrid = contentGrid.Children[2] :?> Grid

  let nameLabeledField = formGrid.Children[0] :?> StackPanel
  let nameTextBox = nameLabeledField.Children[1] :?> TextBox

  let descriptionLabeledField = formGrid.Children[1] :?> StackPanel
  let descriptionTextBox = descriptionLabeledField.Children[1] :?> TextBox

  let driverLabeledField = formGrid.Children[2] :?> StackPanel
  let driverComboBox = driverLabeledField.Children[1] :?> ComboBox

  let connectionLabeledField = formGrid.Children[3] :?> StackPanel
  let connectionTextBox = connectionLabeledField.Children[1] :?> TextBox

  let createButton = formGrid.Children[4] :?> Button

  nameTextBox.Text <- "Test Project"
  descriptionTextBox.Text <- "Test Description"
  connectionTextBox.Text <- "test connection"
  driverComboBox.SelectedItem <- MigrondiDriver.Postgresql

  nameTextBox.RaiseEvent(
    TextChangedEventArgs(TextBox.TextChangedEvent, nameTextBox)
  )

  descriptionTextBox.RaiseEvent(
    TextChangedEventArgs(TextBox.TextChangedEvent, descriptionTextBox)
  )

  connectionTextBox.RaiseEvent(
    TextChangedEventArgs(TextBox.TextChangedEvent, connectionTextBox)
  )

  createButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Avalonia.Threading.Dispatcher.UIThread.RunJobs()

  let expected = {
    name = "Test Project"
    description = "Test Description"
    connection = "test connection"
    driver = MigrondiDriver.Postgresql
    tableName = MigrondiConfig.Default.tableName
  }

  Assert.Equal(Some expected, createdProject)
