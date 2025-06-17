module MigrondiUI.Components.EditableMigration

open System

open Avalonia.Controls
open Avalonia.Media
open IcedTasks
open Migrondi.Core
open NXUI.Extensions

open FSharp.Data.Adaptive
open Navs.Avalonia
open MigrondiUI.Components.Fields

type SaveMigrationArgs = {
  upContent: string
  downContent: string
  migration: Migration
}

let saveMigrationBtn(onSaveRequested: unit -> Async<unit>) =
  let enable = cval true

  Button()
    .Content("Save")
    .IsEnabled(enable |> AVal.toBinding)
    .OnClickHandler(fun _ _ ->
      asyncEx {
        enable.setValue false
        do! onSaveRequested()
        enable.setValue true
      }
      |> Async.StartImmediate)

let removeMigrationBtn(onRemoveRequested: unit -> Async<unit>) =
  let enable = cval true

  Button()
    .Content("Remove 🗑️")
    .IsEnabled(enable |> AVal.toBinding)
    .OnClickHandler(fun _ _ ->
      asyncEx {
        enable.setValue false
        do! onRemoveRequested()
        enable.setValue true
      }
      |> Async.StartImmediate)

type EditableMigrationView
  (
    migrationStatus: MigrationStatus,
    onSaveRequested: Migration -> Async<bool>,
    onRemoveRequested: unit -> Async<unit>
  ) =
  inherit UserControl()

  let migration, upContent, downContent, readonly =
    match migrationStatus with
    | Applied m -> cval m, (cval m.upContent), (cval m.downContent), true
    | Pending m -> cval m, (cval m.upContent), (cval m.downContent), false

  let isDirty =
    (migration, upContent, downContent)
    |||> AVal.map3(fun migration up down ->
      up <> migration.upContent || down <> migration.downContent)

  let status =
    match migrationStatus with
    | Applied _ -> AVal.constant "Applied"
    | Pending _ ->
      isDirty
      |> AVal.map(fun isDirty ->
        if isDirty then
          "[Pending] (Unsaved Changes)"
        else
          "[Pending]")

  let strDate =
    DateTimeOffset.FromUnixTimeMilliseconds(AVal.force migration |> _.timestamp)
    |> _.ToString("G")

  let migrationName = AVal.force migration |> _.name
  let manualTransaction = AVal.force migration |> _.manualTransaction

  let migrationContent =
    Grid()
      .ColumnDefinitions("*,4,*")
      .Children(
        LabeledField
          .Vertical(
            "Migrate Up",
            TextEditor.TxtEditor.ReadWrite(upContent, readonly)
          )
          .Column(0),
        GridSplitter()
          .Column(1)
          .ResizeDirectionColumns()
          .IsEnabled(false)
          .Background("Black" |> SolidColorBrush.Parse)
          .MarginX(8)
          .CornerRadius(5),
        LabeledField
          .Vertical(
            "Migrate Down",
            TextEditor.TxtEditor.ReadWrite(downContent, readonly)
          )
          .Column(2)
      )

  let saveBtn =
    UserControl()
      .Content(
        isDirty
        |> AVal.map (function
          | true ->
            saveMigrationBtn(fun () -> asyncEx {

              let upContent = AVal.force upContent
              let downContent = AVal.force downContent
              let _migration = AVal.force migration

              let m = {
                _migration with
                    upContent = upContent
                    downContent = downContent
              }

              let! saved = onSaveRequested m

              if saved then migration.setValue m else ()
            })
          | false -> null)
        |> AVal.toBinding
      )

  let removeBtn =
    UserControl()
      .Content(
        if readonly then
          null
        else
          removeMigrationBtn(fun () -> asyncEx { do! onRemoveRequested() })
      )

  let centerTextBlock(txt: TextBlock) = txt.VerticalAlignmentCenter()

  do
    base.Content <-
      Expander()
        .Header(
          StackPanel()
            .Spacing(8)
            .Children(
              TextBlock().Text(migrationName) |> centerTextBlock,
              TextBlock()
                .Text(status |> AVal.toBinding)
                .Foreground(
                  match migrationStatus with
                  | Applied _ -> "Green"
                  | Pending _ -> "OrangeRed"
                  |> SolidColorBrush.Parse
                )
              |> centerTextBlock,
              TextBlock().Text(strDate) |> centerTextBlock,
              saveBtn,
              removeBtn
            )
            .OrientationHorizontal()
            .VerticalAlignmentCenter()
        )
        .Content(
          StackPanel()
            .Children(
              LabeledField.Horizontal(
                "Manual Transaction:",
                $"%b{manualTransaction}"
              ),
              migrationContent
            )
            .Spacing(8)
        )
        .HorizontalAlignmentStretch()
        .VerticalAlignmentStretch()
        .MarginY(4)
