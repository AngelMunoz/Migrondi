module MigrondiUI.Components.VirtualProjectForm

open System
open Avalonia.Controls
open NXUI.Extensions
open FSharp.Data.Adaptive
open IcedTasks
open FsToolkit.ErrorHandling
open Migrondi.Core
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Components.Fields

module VirtualProject =

  let setName (name: string | null) (project: VirtualProject cval) =
    Ok name
    |> Result.bind(fun name ->
      if String.IsNullOrWhiteSpace(name) then
        Error "Name string cannot be empty."
      else
        Ok(nonNull name))
    |> Result.toOption
    |> Option.iter(fun name ->
      project.setValue(fun p -> { p with name = name }))

  let setDescription
    (description: string | null)
    (project: VirtualProject cval)
    =
    let description =
      if String.IsNullOrWhiteSpace(description) then
        None
      else
        Some(nonNull description)

    project.setValue(fun p -> { p with description = description })

  let setConnection (connection: string | null) (project: VirtualProject cval) =
    Ok connection
    |> Result.bind(fun conn ->
      if String.IsNullOrWhiteSpace(conn) then
        Error "Connection string cannot be empty."
      else
        Ok(nonNull conn))
    |> Result.toOption
    |> Option.iter(fun connection ->
      project.setValue(fun p -> { p with connection = connection }))

  let setDriver (driver: string) (project: VirtualProject cval) =
    project.setValue(fun p -> {
      p with
          driver = MigrondiDriver.FromString driver
    })


type VirtualProjectForm
  (project: VirtualProject aval, onSave: VirtualProject -> Async<unit>) =
  inherit UserControl()

  let aProject = cval (project |> AVal.force)

  let driverItems = [|
    MigrondiDriver.Mysql.AsString
    MigrondiDriver.Postgresql.AsString
    MigrondiDriver.Mssql.AsString
    MigrondiDriver.Sqlite.AsString
  |]

  let saveBtn =
    let isSaving = cval false

    Button()
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
    base.Content <-
      StackPanel()
        .Spacing(8)
        .Children(
          LabeledField.Vertical(
            "Project Name",
            TextBox()
              .AcceptsReturn(true)
              .Height(60.0)
              .Text(aProject |> AVal.map(_.name) |> AVal.toBinding)
              .OnTextChangedHandler(fun tb _ ->
                aProject |> VirtualProject.setName(tb.Text))
          ),
          LabeledField.Vertical(
            "Description",
            TextBox()
              .AcceptsReturn(true)
              .Height(60.0)
              .Text(
                aProject
                |> AVal.map(fun p -> p.description |> Option.defaultValue "")
                |> AVal.toBinding
              )
              .OnTextChangedHandler(fun tb _ ->
                aProject |> VirtualProject.setDescription(tb.Text))
          ),
          LabeledField.Vertical(
            "Connection String",
            TextBox()
              .Text(aProject |> AVal.map(_.connection) |> AVal.toBinding)
              .OnTextChangedHandler(fun tb _ ->
                aProject |> VirtualProject.setConnection(tb.Text))
          ),
          LabeledField.Vertical(
            "Database Driver",
            ComboBox()
              .ItemsSource(driverItems)
              .SelectedItem(
                aProject |> AVal.map(_.driver.AsString) |> AVal.toBinding
              )
              .OnSelectionChanged<string>(fun ((selected, _), _) ->
                selected
                |> Seq.tryHead
                |> Option.iter(fun s ->
                  aProject |> VirtualProject.setDriver(s)))
          ),
          saveBtn
        )
