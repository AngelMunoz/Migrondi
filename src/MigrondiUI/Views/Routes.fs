namespace MigrondiUI.Views


open Navs
open Navs.Avalonia


module Routes =
  open MigrondiUI.Projects
  open Microsoft.Extensions.Logging

  /// Factory function to create a Landing view and its VM from an IProjectRepository
  let private landingViewWithVM(lf: ILoggerFactory, projects) =
    let logger = lf.CreateLogger<Landing.LandingVM>()
    Landing.View(Landing.LandingVM(logger, projects), logger)

  let GetRouter lf (projects: IProjectRepository) =

    let router: IRouter<_> =
      AvaloniaRouter [
        Route.define("landing", "/", landingViewWithVM(lf, projects))
      ]

    router.Navigate("/")
    |> Async.AwaitTask
    |> Async.Ignore
    |> Async.StartImmediate

    router
