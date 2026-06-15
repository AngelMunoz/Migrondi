module MigrondiUI.Views.NewProject

open System
open System.IO

open Microsoft.Extensions.Logging

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders

open Avalonia.Controls
open Avalonia.Platform.Storage

open NXUI.Extensions

open FSharp.Data.Adaptive
open FsToolkit.ErrorHandling

open Navs
open Navs.Avalonia
open Migrondi.Core
open Migrondi.Core.Serialization
open MigrondiUI
open MigrondiUI.Services
open MigrondiUI.Components.NewVirtualProjectForm
open SukiUI.Controls

type LocalProjectTarget =
  | CreateLocal
  | ImportToVirtual

type NewProjectVM(logger: ILogger<NewProjectVM>, projects: IProjectCollection) =

  do logger.LogDebug "NewProjectVM created"

  member _.LoadLocalProject(view: Control) : Async<Guid option> = asyncOption {
    let! token = Async.CancellationToken
    logger.LogDebug "Loading local project"
    let! topLevel = TopLevel.GetTopLevel(view)

    let! file =
      topLevel.StorageProvider.OpenFilePickerAsync(
        FilePickerOpenOptions(
          Title = "Select Project File",
          AllowMultiple = false,
          FileTypeFilter = [|
            FilePickerFileType(
              "Migrondi Config",
              Patterns = [ "migrondi.json" ]
            )
          |]
        )
      )

    let! file = file |> Seq.tryHead
    let! parentFolder = file.GetParentAsync()
    let! parentFolder = parentFolder
    let name = parentFolder.Name

    logger.LogDebug("Selected file: {File}", file.Name)

    let! pid = projects.RegisterLocal (file.Path.LocalPath, name = name) token

    return pid
  }

  member _.CreateNewLocalProject(view) = asyncOption {
    logger.LogDebug "Creating new local project"
    let! token = Async.CancellationToken
    let! topLevel = TopLevel.GetTopLevel view

    let! directory = asyncOption {
      let! directory =
        topLevel.StorageProvider.OpenFolderPickerAsync(
          FolderPickerOpenOptions(AllowMultiple = false)
        )

      let! selected = directory |> Seq.tryHead

      return! selected.TryGetLocalPath()
    }

    let config = MigrondiConfig.Default

    logger.LogDebug("Selected directory: {Directory}", directory)

    try
      Directory.CreateDirectory(Path.Combine(directory, "migrations")) |> ignore
    with
    | :? IOException
    | :? UnauthorizedAccessException as ex ->

      logger.LogWarning(
        "Failed to create migrations directory: {Message}",
        ex.Message
      )

    let configPath = Path.Combine(directory, "migrondi.json")
    logger.LogDebug("Creating config file in {configfile}", configPath)

    File.WriteAllText(configPath, MiSerializer.Encode config)

    let! dirName = Path.GetFileNameWithoutExtension directory

    logger.LogDebug("Registering local project with name {Name}", dirName)

    let! pid = projects.RegisterLocal(configPath, name = dirName)

    logger.LogDebug("Registered local project with id {Id}", pid)
    return pid
  }

  member _.CreateNewVirtualProject(args: Database.InsertVirtualProjectArgs) = asyncEx {
    logger.LogDebug "Creating new virtual project"

    let! pid = projects.CreateVirtual args
    logger.LogDebug("Inserted virtual project with id {Id}", pid)
    return pid
  }

  member _.ImportToVirtualProject(view: Control) : Async<Guid option> = asyncOption {
    logger.LogDebug "Importing local project to virtual project"
    let! token = Async.CancellationToken
    let! topLevel = TopLevel.GetTopLevel(view)

    let! file =
      topLevel.StorageProvider.OpenFilePickerAsync(
        FilePickerOpenOptions(
          Title = "Select Project File",
          AllowMultiple = false,
          FileTypeFilter = [|
            FilePickerFileType(
              "Migrondi Config",
              Patterns = [ "migrondi.json" ]
            )
          |]
        )
      )

    let! file = file |> Seq.tryHead
    logger.LogDebug("Selected file: {File}", file.Name)

    let! guid = projects.Import file.Path.LocalPath token

    return guid
  }

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
  (vm: NewProjectVM, logger: ILogger)
  _
  (nav: INavigable<Control>)
  : Control =
  let view = UserControl()

  let handleSelectLocalProject(target: LocalProjectTarget) = asyncEx {
    let! projectId = asyncEx {
      return!
        match target with
        | CreateLocal -> vm.LoadLocalProject view
        | ImportToVirtual -> vm.ImportToVirtualProject view
    }

    match projectId with
    | None ->
      logger.LogWarning "No project id returned"
      return ()
    | Some projectId ->

      let target =
        match target with
        | CreateLocal -> "local"
        | ImportToVirtual -> "virtual"

      match! nav.Navigate $"/projects/{target}/{projectId}" with
      | Ok _ -> ()
      | Error(e) ->
        logger.LogWarning("Navigation Failure: {error}", e.StringError())
  }

  let handleCreateNewLocalProject() = asyncEx {
    let! projectId = vm.CreateNewLocalProject view

    match projectId with
    | None ->
      logger.LogWarning "No project id returned"
      return ()
    | Some projectId ->
      match! nav.Navigate $"/projects/local/%s{projectId.ToString()}" with
      | Ok _ -> ()
      | Error(e) ->
        logger.LogWarning("Navigation Failure: {error}", e.StringError())
  }

  let handleCreateVirtualProject(args: Database.InsertVirtualProjectArgs) = asyncEx {
    let! createdId = vm.CreateNewVirtualProject args

    match! nav.Navigate $"/projects/virtual/{createdId}" with
    | Ok _ -> ()
    | Error(e) ->
      logger.LogWarning("Navigation Failure: {error}", e.StringError())
  }

  let tabControl =
    TabControl()
      .ItemsSource(
        TabItem()
          .Header("Local Project")
          .Content(
            localProjectTab(
              (fun () ->
                handleSelectLocalProject CreateLocal |> Async.StartImmediate),
              (fun () -> handleCreateNewLocalProject() |> Async.StartImmediate)
            )
          ),
        TabItem()
          .Header("Virtual Project")
          .Content(
            virtualProjectTab(
              (fun args ->
                handleCreateVirtualProject args |> Async.StartImmediate),
              (fun () ->
                handleSelectLocalProject ImportToVirtual
                |> Async.StartImmediate)
            )
          )
      )

  view.Name("NewProject").Content(GlassCard().Content(tabControl))
