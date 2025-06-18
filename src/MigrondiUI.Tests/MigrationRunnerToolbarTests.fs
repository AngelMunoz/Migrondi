module MigrationRunnerToolbarTests

open MigrondiUI.Components.ProjectDetails
open Xunit
open Avalonia.Controls
open Avalonia.Headless.XUnit
open MigrondiUI.Components.MigrationRunnerToolbar
open FSharp.Data.Adaptive
open Avalonia.Interactivity



[<AvaloniaFact>]
let ``Pending Button with dryRun is false will have a confirm apply button``() =
  let mutable clicked = false
  let mutable clicked2 = false

  let pendingButton =
    applyPendingButton(
      AVal.constant false,
      (fun _ -> clicked <- true),
      (fun () ->
        clicked2 <- true
        0)
    )

  let splitButton = pendingButton.Content :?> SplitButton

  let flyout = splitButton.Flyout :?> Flyout

  let confirmBtn = flyout.Content :?> Button
  Assert.NotNull(confirmBtn)
  Assert.Equal("Confirm Apply", $"{confirmBtn.Content}")

  confirmBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.True(clicked)
  Assert.True(clicked2)

[<AvaloniaFact>]
let ``Pending Button with dryRun is true will have a dry run button``() =
  let mutable clicked = false
  let mutable clicked2 = false

  let pendingButton =
    applyPendingButton(
      AVal.constant true,
      (fun _ -> clicked <- true),
      (fun () ->
        clicked2 <- true
        0)
    )

  let button = pendingButton.Content :?> Button

  Assert.NotNull(button)
  Assert.Equal("Apply Pending (Dry Run)", $"{button.Content}")

  button.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.True(clicked)
  Assert.True(clicked2)


[<AvaloniaFact>]
let ``Rollback Button with dryRun is false will have a confirm rollback button``
  ()
  =
  let mutable clicked = false
  let mutable clicked2 = false

  let buttonControl =
    rollbackButton(
      AVal.constant false,
      (fun _ -> clicked <- true),
      (fun () ->
        clicked2 <- true
        0)
    )

  let splitButton = buttonControl.Content :?> SplitButton

  let flyout = splitButton.Flyout :?> Flyout

  let confirmBtn = flyout.Content :?> Button
  Assert.NotNull(confirmBtn)
  Assert.Equal("Confirm Rollback", $"{confirmBtn.Content}")

  confirmBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.True(clicked)
  Assert.True(clicked2)

[<AvaloniaFact>]
let ``Rollback Button with dryRun is true will have a dry run button``() =
  let mutable clicked = false
  let mutable clicked2 = false

  let buttonControl =
    rollbackButton(
      AVal.constant true,
      (fun _ -> clicked <- true),
      (fun () ->
        clicked2 <- true
        0)
    )

  let button = buttonControl.Content :?> Button

  Assert.NotNull(button)
  Assert.Equal("Rollback (Dry Run)", $"{button.Content}")

  button.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.True(clicked)
  Assert.True(clicked2)

[<AvaloniaFact>]
let ``NumericUpDown will update cval on change``() =
  let steps = cval 1M
  let numUpDown = numericUpDown(steps)

  numUpDown.Value <- 10M

  numUpDown.SetCurrentValue(NumericUpDown.ValueProperty, 10M)

  let actual = steps |> AVal.force

  Assert.Equal(10M, actual)

[<AvaloniaFact>]
let ``CheckBox will update cval on change``() =
  let dryRun = cval false
  let chkBox = checkBox(dryRun)

  chkBox.SetCurrentValue(CheckBox.IsCheckedProperty, true)

  let actual = dryRun |> AVal.force
  Assert.True(actual)

[<AvaloniaFact>]
let ``MigrationsRunnerToolbar - Apply button with dryRun checked calls back with DryUp``
  ()
  =
  let mutable calledKind = Unchecked.defaultof<RunMigrationKind>
  let mutable calledSteps = 0

  let toolbar =
    MigrationsRunnerToolbar(fun (kind, steps) ->
      calledKind <- kind
      calledSteps <- steps)

  // Find the checkbox and check it
  let checkbox =
    toolbar.Content :?> StackPanel |> fun sp -> sp.Children[2] :?> CheckBox

  checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, true)

  // Find and click the apply button
  let applyButton =
    toolbar.Content :?> StackPanel
    |> fun sp -> sp.Children[0] :?> UserControl
    |> fun uc -> uc.Content :?> Button

  applyButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.Equal(RunMigrationKind.DryUp, calledKind)
  Assert.Equal(1, calledSteps) // Default value

[<AvaloniaFact>]
let ``MigrationsRunnerToolbar - Apply button with dryRun unchecked calls back with Up after confirmation``
  ()
  =
  let mutable calledKind = Unchecked.defaultof<RunMigrationKind>
  let mutable calledSteps = 0

  let toolbar =
    MigrationsRunnerToolbar(fun (kind, steps) ->
      calledKind <- kind
      calledSteps <- steps)

  // Find and click the confirm apply button
  let applySplitButton =
    toolbar.Content :?> StackPanel
    |> fun sp -> sp.Children[0] :?> UserControl
    |> fun uc -> uc.Content :?> SplitButton

  let flyout = applySplitButton.Flyout :?> Flyout
  let confirmBtn = flyout.Content :?> Button

  confirmBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.Equal(RunMigrationKind.Up, calledKind)
  Assert.Equal(1, calledSteps) // Default value

[<AvaloniaFact>]
let ``MigrationsRunnerToolbar - Rollback button with dryRun checked calls back with DryDown``
  ()
  =
  let mutable calledKind = Unchecked.defaultof<RunMigrationKind>
  let mutable calledSteps = 0

  let toolbar =
    MigrationsRunnerToolbar(fun (kind, steps) ->
      calledKind <- kind
      calledSteps <- steps)

  // Find the checkbox and check it
  let checkbox =
    toolbar.Content :?> StackPanel |> fun sp -> sp.Children[2] :?> CheckBox

  checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, true)

  // Find and click the rollback button
  let rollbackButton =
    toolbar.Content :?> StackPanel
    |> fun sp -> sp.Children[1] :?> UserControl
    |> fun uc -> uc.Content :?> Button

  rollbackButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.Equal(RunMigrationKind.DryDown, calledKind)
  Assert.Equal(1, calledSteps) // Default value

[<AvaloniaFact>]
let ``MigrationsRunnerToolbar - Rollback button with dryRun unchecked calls back with Down after confirmation``
  ()
  =
  let mutable calledKind = Unchecked.defaultof<RunMigrationKind>
  let mutable calledSteps = 0

  let toolbar =
    MigrationsRunnerToolbar(fun (kind, steps) ->
      calledKind <- kind
      calledSteps <- steps)

  // Find and click the confirm rollback button
  let rollbackSplitButton =
    toolbar.Content :?> StackPanel
    |> fun sp -> sp.Children[1] :?> UserControl
    |> fun uc -> uc.Content :?> SplitButton

  let flyout = rollbackSplitButton.Flyout :?> Flyout
  let confirmBtn = flyout.Content :?> Button

  confirmBtn.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.Equal(RunMigrationKind.Down, calledKind)
  Assert.Equal(1, calledSteps) // Default value

[<AvaloniaFact>]
let ``MigrationsRunnerToolbar - Steps value is passed to callback``() =
  let mutable calledKind = Unchecked.defaultof<RunMigrationKind>
  let mutable calledSteps = 0

  let toolbar =
    MigrationsRunnerToolbar(fun (kind, steps) ->
      calledKind <- kind
      calledSteps <- steps)

  // Set the numeric up/down control value
  let numericUpDown =
    toolbar.Content :?> StackPanel |> fun sp -> sp.Children[3] :?> NumericUpDown

  numericUpDown.SetCurrentValue(NumericUpDown.ValueProperty, 5M)

  // Click the apply button with dry run
  let checkbox =
    toolbar.Content :?> StackPanel |> fun sp -> sp.Children[2] :?> CheckBox

  checkbox.SetCurrentValue(CheckBox.IsCheckedProperty, true)

  let applyButton =
    toolbar.Content :?> StackPanel
    |> fun sp -> sp.Children[0] :?> UserControl
    |> fun uc -> uc.Content :?> Button

  applyButton.RaiseEvent(RoutedEventArgs(Button.ClickEvent))

  Assert.Equal(RunMigrationKind.DryUp, calledKind)
  Assert.Equal(5, calledSteps) // Value we set
