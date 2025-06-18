module Tests

open System
open Xunit
open Avalonia.Controls
open Avalonia.Headless
open Avalonia.Headless.XUnit
open MigrondiUI.Components.MigrationRunnerToolbar
open FSharp.Data.Adaptive
open Avalonia
open Avalonia.Input
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
