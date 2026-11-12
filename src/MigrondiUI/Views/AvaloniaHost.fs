namespace MigrondiUI.Views

open System

open Avalonia.Controls
open Avalonia.Platform.Storage

open IcedTasks
open IcedTasks.Polyfill.Async.PolyfillBuilders
open FsToolkit.ErrorHandling

open Navs
open Navs.Avalonia

open MigrondiUI.Logic

/// Implements the logic navigation seam against the real Avalonia router.
type AvaloniaNavigator(nav: INavigable<Control>) =

  interface INavigator with
    member _.Navigate(url: string) = asyncEx {
      let! result = nav.Navigate url

      return
        match result with
        | Ok _ -> Ok()
        | Error e -> Error(e.StringError())
    }

    member _.NavigateByName(name: string) = asyncEx {
      let! result = nav.NavigateByName name

      return
        match result with
        | Ok _ -> Ok()
        | Error e -> Error(e.StringError())
    }

/// Implements the logic file-picker seam with the platform storage provider.
type StorageProviderFilePicker(viewSource: Control) =

  interface IFilePicker with
    member _.PickProjectConfig() = asyncOption {
      let! topLevel = TopLevel.GetTopLevel(viewSource)

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
      return file.Path.LocalPath
    }

    member _.PickDirectory() = asyncOption {
      let! topLevel = TopLevel.GetTopLevel(viewSource)

      let! directory =
        topLevel.StorageProvider.OpenFolderPickerAsync(
          FolderPickerOpenOptions(AllowMultiple = false)
        )

      let! selected = directory |> Seq.tryHead

      return! selected.TryGetLocalPath()
    }
