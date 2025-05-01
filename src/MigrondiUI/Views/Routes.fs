namespace MigrondiUI.Views


open Navs
open Navs.Avalonia


module Routes =
  open MigrondiUI.Projects

  let GetRouter(_: IProjectRepository) =
    let router: IRouter<_> =
      AvaloniaRouter [ Route.define("landing", "/", Landing.View) ]

    router.Navigate("/")
    |> Async.AwaitTask
    |> Async.Ignore
    |> Async.StartImmediate

    router
