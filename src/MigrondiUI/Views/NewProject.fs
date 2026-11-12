module MigrondiUI.Views.NewProject

open System

open Microsoft.Extensions.Logging

open IcedTasks

open Avalonia.Controls

open NXUI.Extensions

open FSharp.Data.Adaptive

open Navs
open Navs.Avalonia
open MigrondiUI
open MigrondiUI.Services
open MigrondiUI.Logic
open MigrondiUI.Logic.NewProject
open MigrondiUI.Components.NewVirtualProjectForm
open SukiUI.Controls

let private localProjectTab
  (
    handleSelectLocalProject: unit -> unit,
    handleCreateNewLocalProject: unit -> unit
  ) =
  StackPanel()
    .Spacing(10)
    .Margin(10)
    .Children(
      Button()
        .Content("Create New Local Project")
        .OnClickHandler(fun _ _ -> handleCreateNewLocalProject()),
      Button()
        .Content("Select Local Project")
        .OnClickHandler(fun _ _ -> handleSelectLocalProject())
    )

let private virtualProjectTab
  (
    handleCreateVirtualProject: Database.InsertVirtualProjectArgs -> unit,
    handleImportToVirtual: unit -> unit
  ) =
  NewVirtualProjectForm(handleCreateVirtualProject, handleImportToVirtual)


let View
  (logger: ILogger<NewProjectVM>)
  (projects: IProjectCollection)
  _
  (nav: INavigable<Control>)
  : Control =
  let view = UserControl()
  let picker = StorageProviderFilePicker view
  let navigator = AvaloniaNavigator nav
  let vm = NewProjectVM(logger, projects, picker, navigator)

  let tabControl =
    TabControl()
      .ItemsSource(
        TabItem()
          .Header("Local Project")
          .Content(
            localProjectTab(
              (fun () ->
                vm.SelectLocalProject CreateLocal |> Async.StartImmediate),
              (fun () -> vm.CreateNewLocalProject() |> Async.StartImmediate)
            )
          ),
        TabItem()
          .Header("Virtual Project")
          .Content(
            virtualProjectTab(
              (fun args ->
                vm.CreateNewVirtualProject args |> Async.StartImmediate),
              (fun () ->
                vm.SelectLocalProject ImportToVirtual |> Async.StartImmediate)
            )
          )
      )

  view.Name("NewProject").Content(GlassCard().Content(tabControl))
