module MigrondiUI.Components.EditableMigration

open System

open Avalonia
open Avalonia.Controls
open Avalonia.Media
open Avalonia.Styling
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
    .Classes("Primary")
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
    .Classes("Danger")
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
  ) as this =
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
      .Classes("MigrationContentGrid")
      .ColumnDefinitions("*,4,*")
      .Children(
        LabeledField
          .Vertical(
            "Migrate Up",
            TextEditor.TxtEditor.ReadWrite(upContent, readonly)
          )
          .Column(0),
        GridSplitter()
          .Classes("VerticalDivider")
          .Column(1)
          .ResizeDirectionColumns()
          .IsEnabled(false),
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
    base.Classes.Add("EditableMigrationView")

    base.Content <-
      Expander()
        .Classes("EditableMigrationExpander")
        .Header(
          StackPanel()
            .Classes("EditableMigrationHeader")
            .Children(
              TextBlock().Classes("MigrationNameText").Text(migrationName)
              |> centerTextBlock,
              TextBlock()
                .Classes(
                  [|
                    "MigrationStatusText"
                    match migrationStatus with
                    | Applied _ -> "AppliedForeground"
                    | Pending _ -> "PendingForeground"
                  |]
                )
                .Text(status |> AVal.toBinding)
              |> centerTextBlock,
              TextBlock().Classes("MigrationDateText").Text(strDate)
              |> centerTextBlock,
              saveBtn,
              removeBtn
            )
        )
        .Content(
          StackPanel()
            .Classes("EditableMigrationPanel")
            .Children(
              LabeledField.Horizontal(
                "Manual Transaction:",
                $"%b{manualTransaction}"
              ),
              migrationContent
            )
        )

    this.ApplyStyles()

  member private this.ApplyStyles() =
    this.Styles.AddRange [
      // StackPanel styles
      Style()
        .Selector(_.OfType<StackPanel>().Class("EditableMigrationHeader"))
        .SetStackLayoutOrientation(Layout.Orientation.Horizontal)
        .SetStackLayoutSpacing(8)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Center)

      Style()
        .Selector(_.OfType<StackPanel>().Class("EditableMigrationPanel"))
        .SetStackLayoutSpacing(8)

      // Expander styles
      Style()
        .Selector(_.OfType<Expander>().Class("EditableMigrationExpander"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Stretch)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Stretch)
        .SetLayoutableMargin(Thickness(0, 4))

      // TextBlock styles
      Style()
        .Selector(_.OfType<TextBlock>().Class("MigrationNameText"))
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Center)

      Style()
        .Selector(_.OfType<TextBlock>().Class("MigrationStatusText"))
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Center)

      Style()
        .Selector(_.OfType<TextBlock>().Class("AppliedForeground"))
        .SetTextBlockForeground("Green" |> SolidColorBrush.Parse)

      Style()
        .Selector(_.OfType<TextBlock>().Class("PendingForeground"))
        .SetTextBlockForeground("OrangeRed" |> SolidColorBrush.Parse)

      Style()
        .Selector(_.OfType<TextBlock>().Class("MigrationDateText"))
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Center)

      // GridSplitter styles
      let backgroundColor =
        if this.ActualThemeVariant = ThemeVariant.Dark then
          "Gray" |> SolidColorBrush.Parse
        else
          "LightGray" |> SolidColorBrush.Parse

      Style()
        .Selector(_.OfType<GridSplitter>().Class("VerticalDivider"))
        .SetPanelBackground(backgroundColor)
        .SetLayoutableMargin(Thickness(8, 0))
        .SetBorderCornerRadius(CornerRadius(5))
    ]
