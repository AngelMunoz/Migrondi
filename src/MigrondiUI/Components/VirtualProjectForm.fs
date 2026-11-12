module MigrondiUI.Components.VirtualProjectForm

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open NXUI.Extensions
open FSharp.Data.Adaptive
open IcedTasks
open FsToolkit.ErrorHandling
open Migrondi.Core
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Components.Fields
open MigrondiUI.Logic.VirtualProjectState

type VirtualProjectForm
  (project: VirtualProject aval, onSave: VirtualProject -> Async<unit>) as this
  =
  inherit UserControl()

  let aProject = cval(project |> AVal.force)

  let driverItems = [|
    MigrondiDriver.Mysql.AsString
    MigrondiDriver.Postgresql.AsString
    MigrondiDriver.Mssql.AsString
    MigrondiDriver.Sqlite.AsString
  |]

  let saveBtn =
    let isSaving = cval false

    Button()
      .Classes("Primary", "SaveButton")
      .Content("Save Project")
      .IsEnabled(isSaving |> AVal.map not |> AVal.toBinding)
      .OnClickHandler(fun _ _ ->
        asyncEx {
          isSaving.setValue true
          let project = AVal.force aProject
          do! onSave project
          isSaving.setValue false
        }
        |> Async.StartImmediate)

  do
    base.Classes.Add("VirtualProjectForm")

    base.Content <-
      StackPanel()
        .Classes("FormContainer")
        .Children(
          LabeledField.Vertical(
            "Project Name",
            TextBox()
              .Classes([| "FieldBox"; "NameField" |])
              .AcceptsReturn(true)
              .Text(aProject |> AVal.map(_.name) |> AVal.toBinding)
              .OnTextChangedHandler(fun tb _ -> aProject |> setName(tb.Text))
          ),
          LabeledField.Vertical(
            "Description",
            TextBox()
              .Classes([| "FieldBox" |])
              .AcceptsReturn(true)
              .Text(
                aProject
                |> AVal.map(fun p -> p.description |> Option.defaultValue "")
                |> AVal.toBinding
              )
              .OnTextChangedHandler(fun tb _ ->
                aProject |> setDescription(tb.Text))
          ),
          LabeledField.Vertical(
            "Connection String",
            TextBox()
              .Classes([| "FieldBox" |])
              .Text(aProject |> AVal.map(_.connection) |> AVal.toBinding)
              .OnTextChangedHandler(fun tb _ ->
                aProject |> setConnection(tb.Text))
          ),
          LabeledField.Vertical(
            "Database Driver",
            ComboBox()
              .Classes([| "FieldBox" |])
              .ItemsSource(driverItems)
              .SelectedItem(
                aProject |> AVal.map(_.driver.AsString) |> AVal.toBinding
              )
              .OnSelectionChanged<string>(fun ((selected, _), _) ->
                selected
                |> Seq.tryHead
                |> Option.iter(fun s -> aProject |> setDriver(s)))
          ),
          saveBtn
        )

    this.ApplyStyles()

  member private this.ApplyStyles() =
    this.Styles.AddRange [
      // Main form container styles
      Style()
        .Selector(_.OfType<StackPanel>().Class("FormContainer"))
        .SetStackLayoutSpacing(8)
        .SetLayoutableMargin(Thickness(8))

      // TextBox styles
      Style()
        .Selector(_.Class("FieldBox"))
        .SetLayoutableHorizontalAlignment(HorizontalAlignment.Stretch)
        .SetBorderCornerRadius(CornerRadius(3))
        .SetLayoutableHeight(42)

      Style()
        .Selector(_.OfType<TextBox>().Class("NameField"))
        .Setters(Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap))
      Style()
        .Selector(_.OfType<TextBox>().Class("DescriptionField"))
        .Setters(Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap))

      // ComboBox styles
      Style()
        .Selector(_.OfType<ComboBox>().Class("FieldCombo"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Stretch)
        .SetBorderCornerRadius(CornerRadius(3))

      // Save button styles
      Style()
        .Selector(_.OfType<Button>().Class("SaveButton"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Right)
        .SetLayoutableMargin(Thickness(0, 8, 0, 0))
    ]
