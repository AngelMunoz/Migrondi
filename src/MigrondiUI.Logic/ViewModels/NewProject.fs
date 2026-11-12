namespace MigrondiUI.Logic

module NewProject =

  open System
  open System.IO

  open Microsoft.Extensions.Logging

  open IcedTasks
  open IcedTasks.Polyfill.Async.PolyfillBuilders

  open Migrondi.Core
  open Migrondi.Core.Serialization
  open MigrondiUI
  open MigrondiUI.Services

  type LocalProjectTarget =
    | CreateLocal
    | ImportToVirtual

  /// When a command's Async completes, all state writes are complete; a host
  /// may rebuild its view immediately after.
  type NewProjectVM
    (
      logger: ILogger<NewProjectVM>,
      projects: IProjectCollection,
      picker: IFilePicker,
      navigator: INavigator
    ) =

    do logger.LogDebug "NewProjectVM created"

    member _.SelectLocalProject(target: LocalProjectTarget) : Async<unit> =
      asyncEx {
        match target with
        | CreateLocal -> logger.LogDebug "Loading local project"
        | ImportToVirtual ->
          logger.LogDebug "Importing local project to virtual project"

        let! picked = picker.PickProjectConfig()

        match picked with
        | None ->
          logger.LogWarning "No project id returned"
          return ()
        | Some path ->
          logger.LogDebug("Selected file: {File}", Path.GetFileName path)

          let! projectId =
            match target with
            | CreateLocal -> asyncEx {
                let name =
                  match Path.GetDirectoryName path with
                  | null -> "unnamed-project"
                  | dir -> Path.GetFileName dir |> nonNull

                return! projects.RegisterLocal(path, name = name)
              }
            | ImportToVirtual -> asyncEx { return! projects.Import path }

          let url =
            match target with
            | CreateLocal -> Routes.localProjectUrl projectId
            | ImportToVirtual -> Routes.virtualProjectUrl projectId

          match! navigator.Navigate url with
          | Ok _ -> return ()
          | Error(e) ->
            logger.LogWarning("Navigation Failure: {error}", e)
            return ()
      }

    member _.CreateNewLocalProject() : Async<unit> = asyncEx {
      logger.LogDebug "Creating new local project"

      let! picked = picker.PickDirectory()

      match picked with
      | None ->
        logger.LogWarning "No project id returned"
        return ()
      | Some directory ->
        let config = MigrondiConfig.Default

        logger.LogDebug("Selected directory: {Directory}", directory)

        try
          Directory.CreateDirectory(Path.Combine(directory, "migrations"))
          |> ignore
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

        let dirName = Path.GetFileNameWithoutExtension directory |> nonNull

        logger.LogDebug("Registering local project with name {Name}", dirName)

        let! pid = projects.RegisterLocal(configPath, name = dirName)

        logger.LogDebug("Registered local project with id {Id}", pid)

        match! navigator.Navigate(Routes.localProjectUrl pid) with
        | Ok _ -> return ()
        | Error(e) ->
          logger.LogWarning("Navigation Failure: {error}", e)
          return ()
    }

    member _.CreateNewVirtualProject(args: Database.InsertVirtualProjectArgs) =
      asyncEx {
        logger.LogDebug "Creating new virtual project"

        let! pid = projects.CreateVirtual args
        logger.LogDebug("Inserted virtual project with id {Id}", pid)

        match! navigator.Navigate(Routes.virtualProjectUrl pid) with
        | Ok _ -> return ()
        | Error(e) ->
          logger.LogWarning("Navigation Failure: {error}", e)
          return ()
      }
