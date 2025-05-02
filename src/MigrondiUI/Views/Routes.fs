namespace MigrondiUI.Views


open Navs
open Navs.Avalonia


module Routes =
  open MigrondiUI.Projects

  /// Factory function to create a Landing view and its VM from an IProjectRepository
  let private landingViewWithVM projects =
    Landing.View(Landing.LandingVM projects)

  let GetRouter(projects: IProjectRepository) =

    let router: IRouter<_> =
      AvaloniaRouter [
        Route.define("landing", "/", landingViewWithVM projects)
      ]

    router.Navigate("/")
    |> Async.AwaitTask
    |> Async.Ignore
    |> Async.StartImmediate

    router
