namespace MigrondiUI.Views

open Avalonia.Controls
open NXUI.Extensions

open Navs.Avalonia

module Landing =
  let View _ _ =
    UserControl()
      .Tag("Landing")
      .Content(
        StackPanel()
          .Children(
            TextBlock().Text("Welcome to Migrondi!"),
            Button()
              .Content("Create a new project")
              .OnClickHandler(fun _ _ -> printfn "Create a new project!"),
            Button()
              .Content("Open an existing project")
              .OnClickHandler(fun _ _ -> printfn "Open an existing project!"),
            Button()
              .Content("Settings")
              .OnClickHandler(fun _ _ -> printfn "Open settings!"),
            Button().Content("Exit").OnClickHandler(fun _ _ -> printfn "Exit!")
          )
          .Spacing(10)
          .Margin(10)
          .OrientationVertical()
      )
