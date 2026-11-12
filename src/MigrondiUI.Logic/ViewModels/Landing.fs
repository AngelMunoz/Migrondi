namespace MigrondiUI.Logic

module Landing =

  open System

  open Microsoft.Extensions.Logging

  open IcedTasks

  open FSharp.Data.Adaptive

  open MigrondiUI
  open MigrondiUI.Services

  type LandingViewState =
    | EditProjects
    | RemoveProjects
    | Empty

  type LandingVM
    (
      logger: ILogger<LandingVM>,
      projects: IProjectCollection,
      navigator: INavigator
    ) =

    let _projects: Project list cval = cval []

    let viewState = cval Empty

    do logger.LogDebug "LandingVM created"

    member _.Projects: Project list aval = _projects

    member _.LoadProjects() = asyncEx {
      let! result = projects.List()
      result |> _projects.setValue
      return ()
    }

    member _.SetLandingState(state: LandingViewState) =
      logger.LogDebug("Setting landing state to {State}", state)
      viewState.setValue state

    member _.EmptyViewState() =
      logger.LogDebug "Setting landing state to Empty"
      viewState.setValue Empty

    member _.ViewState: aval<LandingViewState> = viewState

    member _.OpenProject(project: Project) : Async<unit> = asyncEx {
      let url =
        match project with
        | Local _ -> Routes.localProjectUrl project.Id
        | Virtual _ -> Routes.virtualProjectUrl project.Id

      match! navigator.Navigate url with
      | Ok _ -> ()
      | Error e -> logger.LogWarning("Navigation Failure: {error}", e)
    }
