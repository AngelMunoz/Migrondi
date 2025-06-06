module MigrondiUI.Components.CreateVirtualProjectView

open Avalonia.Controls
open NXUI.Extensions
open FSharp.Data.Adaptive
open MigrondiUI
open MigrondiUI.Components.Fields
open Migrondi.Core
open Avalonia.Controls.Templates

open Navs.Avalonia
open MigrondiUI.Projects

type CreateVirtualProjectView
  (
    onCreateVirtualProject: NewVirtualProjectArgs -> unit,
    onImportLocalProject: unit -> unit
  ) =
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
        .Text(name.Value)
        .OnTextChangedHandler(fun tb _ -> name.setValue tb.Text)

    let descriptionTextBox =
      TextBox()
        .Text(description.Value)
        .OnTextChangedHandler(fun tb _ -> description.setValue tb.Text)

    let connectionTextBox =
      TextBox()
        .Text(connection.Value)
        .OnTextChangedHandler(fun tb _ -> connection.setValue tb.Text)

    let createBtn =
      Button()
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
      .Classes("CreateVirtualProjectForm")
      .RowDefinitions("Auto,Auto,Auto,Auto")
      .ColumnDefinitions("*,*")
      .VerticalAlignmentTop()
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
      .Classes("CreateVirtualProjectView")
      .RowDefinitions("Auto,Auto,Auto")
      .Children(
        importLocalBtn.Row(0).HorizontalAlignmentCenter().Margin(10),
        notice.Row(1).Margin(10),
        form.Row(2)
      )

  do
    base.Content <- content
    base.Name <- nameof CreateVirtualProjectView
