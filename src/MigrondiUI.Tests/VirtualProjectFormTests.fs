module VirtualProjectFormTests

open System.Diagnostics
open Xunit
open Avalonia.Headless.XUnit
open MigrondiUI.Components.VirtualProjectForm
open Migrondi.Core
open Avalonia.Controls
open FSharp.Data.Adaptive
open System
open Avalonia.Interactivity
open MigrondiUI
open Navs.Avalonia

let createProject() : VirtualProject cval =
  let proj: VirtualProject = {
    id = Guid.NewGuid()
    name = "Test Project"
    description = Some "Test Description"
    connection = "test connection"
    driver = MigrondiDriver.Sqlite
    projectId = Guid.NewGuid()
    tableName = "migrations"
  }

  cval proj

let getControls(view: VirtualProjectForm) =
  let mainPanel = view.Content :?> StackPanel
  let nameField = mainPanel.Children[0] :?> StackPanel
  let nameTextBox = nameField.Children[1] :?> TextBox
  let descriptionField = mainPanel.Children[1] :?> StackPanel
  let descriptionTextBox = descriptionField.Children[1] :?> TextBox
  let connectionField = mainPanel.Children[2] :?> StackPanel
  let connectionTextBox = connectionField.Children[1] :?> TextBox
  let driverField = mainPanel.Children[3] :?> StackPanel
  let driverComboBox = driverField.Children[1] :?> ComboBox
  let saveButton = mainPanel.Children[4] :?> Button

  nameTextBox, descriptionTextBox, connectionTextBox, driverComboBox, saveButton

[<AvaloniaFact>]
let ``VirtualProjectForm - should update project name``() =
  let project = createProject()
  let mutable savedProject: MigrondiUI.VirtualProject option = None
  let onSave p = async { savedProject <- Some p }
  let view = VirtualProjectForm(project, onSave)
  let nameTextBox, _, _, _, saveButton = getControls view

  nameTextBox.SetCurrentValue(TextBox.TextProperty, "New Project Name")

  nameTextBox.RaiseEvent(
    TextChangedEventArgs(TextBox.TextChangedEvent, nameTextBox)
  )

  saveButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
  Avalonia.Threading.Dispatcher.UIThread.RunJobs()
  Assert.True(savedProject.IsSome)
  Assert.Equal("New Project Name", savedProject.Value.name)

[<AvaloniaFact>]
let ``VirtualProjectForm - should update project description``() =
  let project = createProject()
  let mutable savedProject = Unchecked.defaultof<MigrondiUI.VirtualProject>

  let onSave p = async {
    do savedProject <- p
    Debug.WriteLine($"Project {project}")
  }

  let view = VirtualProjectForm(project, onSave)
  let _, descriptionTextBox, _, _, saveButton = getControls view

  descriptionTextBox.Text <- "New Description"

  descriptionTextBox.RaiseEvent(
    TextChangedEventArgs(TextBox.TextChangedEvent, descriptionTextBox)
  )

  saveButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
  Avalonia.Threading.Dispatcher.UIThread.RunJobs()
  Assert.Equal("New Description", savedProject |> _.description.Value)

[<AvaloniaFact>]
let ``VirtualProjectForm - should update project connection``() =
  let project = createProject()
  let mutable savedProject: MigrondiUI.VirtualProject option = None
  let onSave p = async { savedProject <- Some p }
  let view = VirtualProjectForm(project, onSave)
  let _, _, connectionTextBox, _, saveButton = getControls view

  connectionTextBox.Text <- "New Connection"
  // Manually trigger TextChanged event to ensure the handler updates the project
  connectionTextBox.RaiseEvent(
    TextChangedEventArgs(TextBox.TextChangedEvent, connectionTextBox)
  )

  saveButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
  Avalonia.Threading.Dispatcher.UIThread.RunJobs()
  Assert.True(savedProject.IsSome)
  Assert.Equal("New Connection", savedProject.Value.connection)

[<AvaloniaFact>]
let ``VirtualProjectForm - should update project driver``() =
  let project = createProject()
  let mutable savedProject: MigrondiUI.VirtualProject option = None
  let onSave p = async { savedProject <- Some p }
  let view = VirtualProjectForm(project, onSave)
  let _, _, _, driverComboBox, saveButton = getControls view

  driverComboBox.SelectedItem <- MigrondiDriver.Postgresql.AsString
  saveButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
  Avalonia.Threading.Dispatcher.UIThread.RunJobs()
  Assert.True(savedProject.IsSome)
  Assert.Equal(MigrondiDriver.Postgresql, savedProject.Value.driver)

[<AvaloniaFact>]
let ``VirtualProjectForm - save button should call onSave``() =
  let mutable onSaveCalled = false
  let project = createProject()
  let onSave _ = async { onSaveCalled <- true }
  let view = VirtualProjectForm(project, onSave)
  let _, _, _, _, saveButton = getControls view

  saveButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))
  Avalonia.Threading.Dispatcher.UIThread.RunJobs()
  Assert.True(onSaveCalled)
