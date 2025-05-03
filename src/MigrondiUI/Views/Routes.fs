namespace MigrondiUI.Views


open Navs
open Navs.Avalonia


module Routes =
  open MigrondiUI.Projects
  open Microsoft.Extensions.Logging

  let private landingViewWithVM(lf: ILoggerFactory, projects) =
    let logger = lf.CreateLogger<Landing.LandingVM>()
    Landing.View(Landing.LandingVM(logger, projects), logger)

  let private projectDetailsViewWithVM(lf: ILoggerFactory, projects) =
    let logger = lf.CreateLogger<ProjectDetails.ProjectDetailsVM>()
    ProjectDetails.View(logger, projects)

  let GetRouter lf projects =

    let router: IRouter<_> =
      AvaloniaRouter [
        Route.define("landing", "/", landingViewWithVM(lf, projects))
        Route.define(
          "project-details",
          "/projects/:projectId<guid>",
          projectDetailsViewWithVM(lf, projects)
        )
        |> Route.cache NoCache
      ]

    router.Navigate("/")
    |> Async.AwaitTask
    |> Async.Ignore
    |> Async.StartImmediate

    router
