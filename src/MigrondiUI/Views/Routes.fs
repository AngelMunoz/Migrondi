module MigrondiUI.Views.Routes


open Microsoft.Extensions.Logging
open Avalonia.Controls

open NXUI.Extensions

open Navs
open Navs.Avalonia



let private landingViewWithVM(lf: ILoggerFactory, projects) =
  let logger = lf.CreateLogger<Landing.LandingVM>()
  Landing.View(Landing.LandingVM(logger, projects), logger)

let private projectDetailsViewWithVM(lf: ILoggerFactory, projects) =
  let logger = lf.CreateLogger<LocalProjectDetails.LocalProjectDetailsVM>()
  let mLogger = lf.CreateLogger<Migrondi.Core.IMigrondi>()
  LocalProjectDetails.View(logger, mLogger, projects)

let private vProjectDetailsViewWithVM() =
  fun (_: RouteContext) (_: INavigable<_>) ->
    TextBlock().Text("Virtual project details view not implemented yet")


let GetRouter lf projects =

  let router: IRouter<_> =
    AvaloniaRouter [
      Route.define("landing", "/", landingViewWithVM(lf, projects))
      Route.define(
        "local-project-details",
        "/projects/local/:projectId<guid>",
        projectDetailsViewWithVM(lf, projects)
      )
      |> Route.cache NoCache
      Route.define(
        "virtual-project-details",
        "/projects/virtual/:projectId<guid>",
        vProjectDetailsViewWithVM()
      )
      |> Route.cache NoCache
    ]

  router.Navigate("/")
  |> Async.AwaitTask
  |> Async.Ignore
  |> Async.StartImmediate

  router
