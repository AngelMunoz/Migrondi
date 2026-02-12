module MigrondiUI.Components.NewVirtualProjectForm

open Avalonia
open Avalonia.Controls
open NXUI.Extensions
open FSharp.Data.Adaptive
open MigrondiUI.Components.Fields
open Migrondi.Core
open Avalonia.Controls.Templates
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Styling
open Navs.Avalonia
open MigrondiUI.Projects

type NewVirtualProjectForm
  (
    onCreateVirtualProject: NewVirtualProjectArgs -> unit,
    onImportLocalProject: unit -> unit
  ) as this =
  inherit UserControl()

  let name = cval ""
  let description = cval ""
  let connection = cval "Data Source=./migrondi.db"
  let driver = cval MigrondiDriver.Sqlite

  let driverOptions = [
    MigrondiDriver.Sqlite
    MigrondiDriver.Postgresql
    MigrondiDriver.Mysql
    MigrondiDriver.Mssql
  ]

  let driverCombo =
    ComboBox()
      .Classes("FieldBox")
      .ItemsSource(driverOptions)
      .SelectedIndex(0)
      .ItemTemplate(
        FuncDataTemplate<MigrondiDriver>(fun driver _ ->
          TextBlock().Text(driver.AsString))
      )
      .OnSelectionChanged<MigrondiDriver>(fun (args, _) ->
        args |> fst |> Seq.tryHead |> Option.iter(AVal.setValue driver))

  let form =
    let nameTextBox =
      TextBox()
        .Classes("FieldBox")
        .Text(name.Value)
        .OnTextChangedHandler(fun tb _ -> name.setValue tb.Text)

    let descriptionTextBox =
      TextBox()
        .Classes("FieldBox")
        .Text(description.Value)
        .OnTextChangedHandler(fun tb _ -> description.setValue tb.Text)

    let connectionTextBox =
      TextBox()
        .Classes("FieldBox")
        .Text(connection.Value)
        .OnTextChangedHandler(fun tb _ -> connection.setValue tb.Text)

    let createBtn =
      Button()
        .Classes("Primary")
        .Content("Create")
        .IsEnabled(
          (name, connection)
          ||> AVal.map2(fun n c -> n.Trim() <> "" && c.Trim() <> "")
          |> AVal.toBinding
        )
        .OnClickHandler(fun _ _ ->
          onCreateVirtualProject {
            name = name.getValue()
            description = description.getValue()
            connection = connection.getValue()
            driver = driver.getValue()
            tableName = MigrondiConfig.Default.tableName
          })

    Grid()
      .Classes("NewVirtualProjectForm")
      .RowDefinitions("Auto,Auto,Auto,Auto")
      .ColumnDefinitions("*,*")
      .VerticalAlignment(VerticalAlignment.Top)
      .Children(
        LabeledField
          .Vertical("Project Name:", nameTextBox)
          .MarginRight(4)
          .Row(0)
          .Column(0),
        LabeledField
          .Vertical("Description:", descriptionTextBox)
          .MarginLeft(4)
          .Row(0)
          .Column(1),
        LabeledField
          .Vertical("Driver:", driverCombo.HorizontalAlignmentStretch())
          .HorizontalAlignmentStretch()
          .Row(1)
          .Column(0)
          .ColumnSpan(2),
        LabeledField
          .Vertical("Connection String:", connectionTextBox)
          .Row(2)
          .Column(0)
          .ColumnSpan(2),
        createBtn
          .Row(3)
          .Column(1)
          .ColumnSpan(2)
          .HorizontalAlignmentRight()
          .MarginY(10)
      )

  let importLocalBtn =
    Button()
      .Content("Import Local Project")
      .OnClickHandler(fun _ _ -> onImportLocalProject())

  let notice =
    TextBlock()
      .Text(
        "You can create a new virtual project or import an existing local project."
      )
      .Margin(10)

  let content =
    Grid()
      .Classes("NewVirtualProjectView")
      .RowDefinitions("Auto,Auto,Auto")
      .Children(
        importLocalBtn.Row(0).HorizontalAlignmentCenter().Margin(10),
        notice.Row(1).Margin(10),
        form.Row(2)
      )

  do
    base.Content <- content
    base.Name <- nameof NewVirtualProjectForm
    this.ApplyStyles()

  member private this.ApplyStyles() =
    this.Styles.AddRange [
      Style()
        .Selector(_.OfType<Grid>().Class("NewVirtualProjectForm"))
        .SetLayoutableMargin(Thickness(8.0))

      Style()
        .Selector(_.Class("FieldBox"))
        .SetLayoutableHorizontalAlignment(HorizontalAlignment.Stretch)
        .SetBorderCornerRadius(CornerRadius(3.0))
        .SetLayoutableHeight(42.0)

      Style()
        .Selector(_.OfType<Button>())
        .SetLayoutableHorizontalAlignment(HorizontalAlignment.Right)
        .SetLayoutableMargin(Thickness(0.0, 8.0, 0.0, 0.0))
    ]
