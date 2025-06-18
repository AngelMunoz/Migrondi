module MigrondiUI.Components.ProjectDetails

open System
open Avalonia.Controls
open Avalonia.Styling
open Avalonia.Controls.Templates
open Avalonia.Media
open AvaloniaEdit

open NXUI.Extensions

open FSharp.Data.Adaptive
open Navs.Avalonia
open Migrondi.Core

open MigrondiUI.Components.Fields
open MigrondiUI.Components.TextEditor
open Avalonia


[<Struct>]
type RunMigrationKind =
  | Up
  | Down
  | DryUp
  | DryDown

type CurrentShow =
  | Migrations
  | DryRun of RunMigrationKind
  | ExceptionThrown of exn

type MigrationStatusView(migrationStatus: MigrationStatus) as this =
  inherit UserControl()

  let migration =
    match migrationStatus with
    | Applied m -> m
    | Pending m -> m

  let status =
    match migrationStatus with
    | Applied _ -> "Applied"
    | Pending _ -> "Pending"

  let strDate =
    DateTimeOffset.FromUnixTimeMilliseconds migration.timestamp
    |> _.ToString("G")

  do
    base.Classes.Add("MigrationStatus")

    base.Content <-
      Expander()
        .Header(
          StackPanel()
            .Classes("MigrationStatusHeader")
            .Children(
              TextBlock().Classes("MigrationName").Text(migration.name),
              TextBlock().Classes($"StatusText_{status}").Text($" [{status}]"),
              TextBlock().Classes("MigrationDate").Text($" - {strDate}")
            )
        )
        .Content(
          StackPanel()
            .Classes("MigrationContent")
            .Children(
              LabeledField.Horizontal(
                "Manual Transaction:",
                $" %b{migration.manualTransaction}"
              ),
              Grid()
                .Classes("MigrationContentGrid")
                .ColumnDefinitions("*,4,*")
                .Children(
                  LabeledField
                    .Vertical(
                      "Migrate Up",
                      TxtEditor.Readonly migration.upContent
                    )
                    .Column(0),
                  GridSplitter()
                    .Classes("Divider")
                    .Column(1)
                    .ResizeDirectionColumns()
                    .IsEnabled(false),
                  LabeledField
                    .Vertical(
                      "Migrate Down",
                      TxtEditor.Readonly migration.downContent
                    )
                    .Column(2)
                )
            )
        )

    this.StyleUp()


  member this.StyleUp() =
    this.Styles.AddRange [
      Style()
        .Selector(_.OfType<Expander>().Class("MigrationStatus"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Stretch)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Stretch)
        .SetLayoutableMargin(Thickness(0, 4))

      Style()
        .Selector(_.OfType<StackPanel>().Class("MigrationStatusHeader"))
        .SetStackLayoutOrientation(Layout.Orientation.Horizontal)

      Style()
        .Selector(_.OfType<TextBlock>().Class("StatusText_Applied"))
        .SetTextBlockForeground("Green" |> SolidColorBrush.Parse)

      Style()
        .Selector(_.OfType<TextBlock>().Class("StatusText_Pending"))
        .SetTextBlockForeground("OrangeRed" |> SolidColorBrush.Parse)

      Style()
        .Selector(_.OfType<StackPanel>().Class("MigrationContent"))
        .SetStackLayoutSpacing(8)

      Style()
        .Selector(_.OfType<GridSplitter>().Class("Divider"))
        .SetPanelBackground("Black" |> SolidColorBrush.Parse)
        .SetLayoutableMargin(Thickness(8, 0))
        .SetBorderCornerRadius(CornerRadius(5))
    ]



type DryRunView(show: RunMigrationKind, migration: Migration) as this =
  inherit UserControl()

  let content =
    match show with
    | Up
    | DryUp -> migration.upContent
    | Down
    | DryDown -> migration.downContent

  let content =
    if migration.manualTransaction then
      content
    else
      $"-- ----------START TRANSACTION----------\n{content}\n-- ----------COMMIT TRANSACTION----------"

  do
    base.Content <- TxtEditor.Readonly(content).Name("MigrationContent")

    this.StyleUp()

  member this.StyleUp() =
    this.Styles.AddRange [
      Style()
        .Selector(_.OfType<TextEditor>().Name("MigrationContent"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Stretch)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Stretch)
    ]

let migrationListView(migrations: MigrationStatus[]) : Control =
  if migrations.Length = 0 then
    TextBlock().Text "No migrations found." :> Control
  else
    ItemsControl()
      .ItemsSource(migrations)
      .ItemTemplate(
        FuncDataTemplate<MigrationStatus>(fun migrationStatus _ ->
          MigrationStatusView migrationStatus)
      )

let templatedMigrationListView
  (tpl: IDataTemplate)
  (migrations: MigrationStatus[])
  : Control =
  if migrations.Length = 0 then
    TextBlock().Text "No migrations found." :> Control
  else
    ItemsControl().ItemsSource(migrations).ItemTemplate(tpl)

let dryRunListView(currentShow, migrations: Migration[]) : Control =
  if migrations.Length = 0 then
    TextBlock().Text "No dry run found."
  else
    match currentShow with
    | DryRun kind ->
      let direction =
        match kind with
        | Up
        | DryUp -> "Apply Migrations"
        | Down
        | DryDown -> "Rollback Migrations"

      StackPanel()
        .Classes("DryRunListView")
        .Children(
          TextBlock().Classes("DryRunHeader").Text
            $"This is a simulation {direction}. The database will not be affected:",
          ItemsControl()
            .ItemsSource(migrations |> Array.map(fun m -> m, kind))
            .ItemTemplate(
              FuncDataTemplate<Migration * RunMigrationKind>
                (fun (migration, kind) _ -> DryRunView(kind, migration))
            ),
          TextBlock().Text
            "Simulation completed. No changes were made to the database."
        )
    | Migrations
    | ExceptionThrown _ -> TextBlock().Text "No dry run found."

let migrationDisplay(migration: Migration) : Control =
  Grid()
    .ColumnDefinitions("*,4,*")
    .RowDefinitions("Auto,*")
    .Children(
      LabeledField.Vertical("Migration Name", migration.name).Column(0),
      GridSplitter()
        .Classes("MigrationDisplaySplitter")
        .Column(1)
        .ResizeDirectionColumns()
        .IsEnabled(false),
      LabeledField
        .Vertical("Manual Transaction", $"{migration.manualTransaction}")
        .Column(2),
      LabeledField
        .Vertical("Up Content", TxtEditor.Readonly migration.upContent)
        .Row(1)
        .Column(0),
      LabeledField
        .Vertical("Down Content", TxtEditor.Readonly migration.downContent)
        .Row(1)
        .Column(2)
    )

let malformedSource
  (content: string, reason: string, name: string voption)
  : Control =
  StackPanel()
    .Classes("MigrondiExceptionView_MalformedSource")
    .Children(
      LabeledField.Horizontal("Reason", reason),
      LabeledField.Vertical("Content", content),
      match name with
      | ValueSome name -> LabeledField.Horizontal("Name", name) :> Control
      | ValueNone -> TextBlock().Text("No name provided.")
    )

let sourceNotFoundDisplay(path: string, name: string) : Control =
  StackPanel()
    .Classes("MigrondiExceptionView_SourceNotFound")
    .Children(
      LabeledField.Horizontal("Path", path),
      LabeledField.Horizontal("Name", name)
    )

type MigrondiExceptionView(error: exn) as this =
  inherit UserControl()

  let errorText =
    match error with
    | :? SetupDatabaseFailed as me -> me.Message
    | :? MigrationApplicationFailed as me -> me.Message
    | :? MigrationRollbackFailed as me -> me.Message
    | :? SourceNotFound as me -> me.Message
    | :? DeserializationFailed as me -> me.Message
    | :? MalformedSource as me -> me.Message
    | _ -> error.Message

  do
    base.Classes.Add("MigrondiExceptionView")

    base.Content <-
      StackPanel()
        .Classes("MigrondiExceptionView_Grid")
        .Children(
          TextBlock()
            .Classes("MigrondiExceptionView_Header")
            .Text("An error occurred while processing the migration:"),
          (match error with
           | :? MigrationApplicationFailed as me ->
             migrationDisplay me.Migration
           | :? MigrationRollbackFailed as me -> migrationDisplay me.Migration
           | :? SourceNotFound as me -> sourceNotFoundDisplay(me.path, me.name)
           | :? DeserializationFailed as me ->
             malformedSource(me.Content, me.Reason, ValueNone)
           | :? MalformedSource as me ->
             malformedSource(me.Content, me.Reason, ValueSome me.SourceName)
           | _ -> TextBlock().Text "No Error details available." :> Control),
          Expander()
            .Classes("MigrondiExceptionView_Expander")
            .Header("Caught Exception (for more details check the app logs):")
            .Content(
              TextBlock()
                .Classes("MigrondiExceptionView_ErrorText")
                .Text(errorText)
            )
        )

    this.StyleUp()


  member this.StyleUp() =
    this.Styles.AddRange [
      Style()
        .Selector(_.OfType<StackPanel>().Class("MigrondiExceptionView_Grid"))
        .SetStackLayoutSpacing(8)
      Style()
        .Selector(_.OfType<TextBlock>().Class("MigrondiExceptionView_Header"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Stretch)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Stretch)
      Style()
        .Selector(_.OfType<Expander>().Class("MigrondiExceptionView_Expander"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Stretch)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Stretch)
      Style()
        .Selector(_.OfType<TextBlock>().Class("MigrondiExceptionView_ErrorText"))
        .SetLayoutableHorizontalAlignment(Layout.HorizontalAlignment.Stretch)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Stretch)
        .SetTextBlockForeground("Red" |> SolidColorBrush.Parse)
      Style()
        .Selector(_.OfType<GridSplitter>().Class("MigrationDisplaySplitter"))
        .SetPanelBackground("Black" |> SolidColorBrush.Parse)
        .SetLayoutableMargin(Thickness(8, 0))
        .SetBorderCornerRadius(CornerRadius(5))
      Style()
        .Selector(_.OfType<StackPanel>().Class("MigrondiExceptionView_MalformedSource"))
        .SetStackLayoutSpacing(8)
      Style()
        .Selector(_.OfType<StackPanel>().Class("MigrondiExceptionView_SourceNotFound"))
        .SetStackLayoutSpacing(8)
    ]

type MigrationsPanel
  (
    ?currentShow: CurrentShow aval,
    ?migrations: MigrationStatus[] aval,
    ?lastDryRun: Migration[] aval,
    ?migrationsView: MigrationStatus[] -> Control,
    ?dryRunView: CurrentShow * Migration[] -> Control
  ) as this =
  inherit UserControl()

  let migrationsView =
    migrationsView
    |> Option.defaultWith(fun () -> failwith "MigrationsView is not set")

  let dryRunView =
    dryRunView |> Option.defaultWith(fun () -> failwith "DryRunView is not set")

  let content =
    let currentShow = defaultArg currentShow (AVal.constant Migrations)
    let migrations = defaultArg migrations (AVal.constant [||])
    let lastDryRun = defaultArg lastDryRun (AVal.constant [||])

    (currentShow, migrations, lastDryRun)
    |||> AVal.map3(fun show mStatus drMigrations ->
      match show with
      | Migrations -> migrationsView mStatus
      | DryRun _ -> dryRunView(show, drMigrations)
      | ExceptionThrown e -> MigrondiExceptionView(e))

  do
    base.Classes.Add("MigrationsPanel")
    base.Content <- ScrollViewer().Content(content |> AVal.toBinding)
    this.StyleUp()


  member this.StyleUp() =
    this.Styles.AddRange [
      Style()
        .Selector(_.OfType<StackPanel>().Class("DryRunListView"))
        .SetStackLayoutSpacing(12)
      Style()
        .Selector(_.OfType<TextBlock>().Class("DryRunHeader"))
        .SetTextBlockFontWeight(FontWeight.Bold)
    ]
