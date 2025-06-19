module MigrondiUI.Views.Routes


open System
open Microsoft.Extensions.Logging

open Avalonia.Controls
open Avalonia.Interactivity
open NXUI
open NXUI.Extensions

open Navs.Avalonia

open MigrondiUI.MigrondiExt
open SukiUI.Controls

[<NoComparison; NoEquality>]
type AppEnvironment = {
  lf: ILoggerFactory
  lProjects: MigrondiUI.Projects.ILocalProjectRepository
  vProjects: MigrondiUI.Projects.IVirtualProjectRepository
  vfs: MigrondiUI.VirtualFs.MigrondiUIFs
  vMigrondiFactory: Migrondi.Core.MigrondiConfig * string * Guid -> IMigrondiUI
  dialogManager: SukiUI.Dialogs.ISukiDialogManager
  toastManager: SukiUI.Toasts.ISukiToastManager
  window: SukiUI.Controls.SukiWindow
}


let private landingView appEnvironment =
  let {
        lf = lf
        lProjects = lProjects
        vProjects = vProjects
        vfs = vfs
      } =
    appEnvironment

  let logger = lf.CreateLogger<Landing.LandingVM>()
  Landing.View(Landing.LandingVM(logger, lProjects, vProjects, vfs), logger)

let private projectDetailsView appEnvironment =
  let { lf = lf; lProjects = projects } = appEnvironment
  let logger = lf.CreateLogger<LocalProjectDetails.LocalProjectDetailsVM>()
  let mLogger = lf.CreateLogger<Migrondi.Core.IMigrondi>()
  LocalProjectDetails.View(logger, mLogger, projects)

let private vProjectDetailsView appEnvironment =
  let {
        lf = lf
        vProjects = vProjects
        vMigrondiFactory = vMigrondiFactory
      } =
    appEnvironment

  let logger = lf.CreateLogger<VirtualProjectDetails.VirtualProjectDetailsVM>()
  VirtualProjectDetails.View(logger, vProjects, vMigrondiFactory)



type SidenavRoutes =
  | ProjectList
  | NewProject


type MigrondiUIAppHost(env: AppEnvironment) =
  inherit UserControl()

  let routesLogger = env.lf.CreateLogger<Routes>()

  let content =
    Routes(logger = routesLogger)
      .Children(
        Route("landing", "/", landingView env),
        Route("new-project", "/projects/new", landingView env),
        Route(
          "local-project-details",
          "/projects/local/:projectId<guid>",
          projectDetailsView env
        ),
        Route(
          "virtual-project-details",
          "/projects/virtual/:projectId<guid>",
          vProjectDetailsView env
        )
      )

  let sideMenu =
    SukiSideMenu()
      .HeaderContent(
        Border()
          .MarginX(8)
          .Child(TextBlock().Classes("h3", "Primary").Text("Migrondi Projects"))
      )
      .FooterContent(
        Border()
          .MarginX(8)
          .Child(
            TextBlock().Classes("h4", "Caption").Text("Powered by MigrondiUI")
          )
      )
      .MenuItems(
        SukiSideMenuItem()
          .Tag(ProjectList)
          .Header("Projects")
          .PageContent(content),
        SukiSideMenuItem()
          .Tag(NewProject)
          .Header("New Project")
          .PageContent(content)
      )

  do
    sideMenu.ObserveSelectedItem()
    |> Observable.add(fun item ->
      match item with
      | :? SukiSideMenuItem as menuItem ->
        routesLogger.LogInformation(
          "Selected menu item: {Header}",
          menuItem.Header
        )

        match menuItem.Tag with
        | :? SidenavRoutes as route when route = ProjectList ->
          content.Router.Value.NavigateByName("landing")
          |> Async.AwaitTask
          |> Async.Ignore
          |> Async.StartImmediate
        | :? SidenavRoutes as route when route = NewProject ->
          content.Router.Value.NavigateByName("new-project")
          |> Async.AwaitTask
          |> Async.Ignore
          |> Async.StartImmediate
        | _ ->
          routesLogger.LogWarning(
            "Selected item does not match any known route: {Tag}",
            menuItem.Tag
          )
      | o ->
        routesLogger.LogWarning(
          "Selected item is not a SukiSideMenuItem: {Item}",
          o
        ))

    base.Classes.Add(nameof MigrondiUIAppHost)
    base.Name <- nameof MigrondiUIAppHost

    base.Content <- sideMenu
