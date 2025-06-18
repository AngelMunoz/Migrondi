module MigrondiUI.Components.MigrationRunnerToolbar

open System

open Avalonia
open Avalonia.Controls
open Avalonia.Styling
open FSharp.Data.Adaptive
open NXUI.Extensions
open Navs.Avalonia
open MigrondiUI.Components.ProjectDetails

let applyPendingButton(dryRun, onRunMigrationsRequested, getIntValue) =
  let btn =
    dryRun
    |> AVal.map(fun dryRun ->
      if dryRun then
        Button()
          .Content("Apply Pending (Dry Run)")
          .OnClickHandler(fun _ _ ->
            onRunMigrationsRequested(RunMigrationKind.DryUp, getIntValue()))
        :> Control
      else
        SplitButton()
          .Content("Apply Pending")
          .Flyout(
            Flyout()
              .Content(
                Button()
                  .Content("Confirm Apply")
                  .OnClickHandler(fun _ _ ->
                    onRunMigrationsRequested(
                      RunMigrationKind.Up,
                      getIntValue()
                    ))
              )
          ))

  UserControl().Name("ApplyPendingButton").Content(btn |> AVal.toBinding)

let rollbackButton(dryRun, onRunMigrationsRequested, getIntValue) =
  let btn =
    dryRun
    |> AVal.map(fun dryRun ->
      if dryRun then
        Button()
          .Content("Rollback (Dry Run)")
          .OnClickHandler(fun _ _ ->
            onRunMigrationsRequested(RunMigrationKind.DryDown, getIntValue()))
        :> Control
      else
        SplitButton()
          .Content("Rollback")
          .Flyout(
            Flyout()
              .Content(
                Button()
                  .Content("Confirm Rollback")
                  .OnClickHandler(fun _ _ ->
                    onRunMigrationsRequested(
                      RunMigrationKind.Down,
                      getIntValue()
                    ))
              )
          ))

  UserControl().Name("RollbackButton").Content(btn |> AVal.toBinding)

let numericUpDown(steps: _ cval) =
  NumericUpDown()
    .Minimum(0)
    .Value(steps |> AVal.toBinding)
    .Watermark("Amount to run")
    .OnValueChangedHandler(fun _ value ->
      match value.NewValue |> ValueOption.ofNullable with
      | ValueNone -> steps.setValue 1M
      | ValueSome value -> steps.setValue value)

let checkBox(dryRun: _ cval) =
  CheckBox()
    .Content("Dry Run")
    .IsChecked(dryRun |> AVal.toBinding)
    .OnIsCheckedChangedHandler(fun checkbox _ ->

      let isChecked =
        checkbox.IsChecked
        |> ValueOption.ofNullable
        |> ValueOption.defaultValue true

      dryRun.setValue isChecked)



type MigrationsRunnerToolbar
  (onRunMigrationsRequested: RunMigrationKind * int -> unit) as this =
  inherit UserControl()
  let dryRun = cval false
  let steps = cval 1M

  let getIntValue() =
    try
      let v = steps.getValue() |> int
      if v < 0 then 1 else v
    with :? OverflowException ->
      1

  do
    base.Classes.Add("MigrationsRunnerToolbar")

    base.Content <-
      StackPanel()
        .Classes("MigrationsRunnerToolbarPanel")
        .Children(
          applyPendingButton(dryRun, onRunMigrationsRequested, getIntValue),
          rollbackButton(dryRun, onRunMigrationsRequested, getIntValue),
          checkBox(dryRun),
          numericUpDown(steps)
        )

    this.ApplyStyles()

  member private this.ApplyStyles() =
    this.Styles.AddRange [
      // Main toolbar panel styles
      Style()
        .Selector(_.OfType<StackPanel>().Class("MigrationsRunnerToolbarPanel"))
        .SetStackLayoutOrientation(Layout.Orientation.Horizontal)
        .SetStackLayoutSpacing(8)
        .SetLayoutableMargin(Thickness(0, 4, 0, 8))

      // Button styles
      Style()
        .Selector(_.OfType<UserControl>().Name("ApplyPendingButton"))
        .SetLayoutableMargin(Thickness(0, 0, 4, 0))

      Style()
        .Selector(_.OfType<UserControl>().Name("RollbackButton"))
        .SetLayoutableMargin(Thickness(0, 0, 8, 0))

      // CheckBox styles
      Style()
        .Selector(_.OfType<CheckBox>())
        .SetLayoutableMargin(Thickness(0, 0, 8, 0))
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Center)

      // NumericUpDown styles
      Style()
        .Selector(_.OfType<NumericUpDown>())
        .SetLayoutableWidth(120)
        .SetLayoutableVerticalAlignment(Layout.VerticalAlignment.Center)
    ]
