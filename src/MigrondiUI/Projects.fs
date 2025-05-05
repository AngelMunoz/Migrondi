module MigrondiUI.Projects

open System
open System.Data
open System.IO
open System.Text.Json
open System.Threading.Tasks

open FsToolkit.ErrorHandling
open Migrondi.Core


type IProjectRepository =
  abstract member GetProjects: unit -> Task<Project list>
  abstract member GetProjectById: Guid -> Task<Project option>

  abstract member InsertVirtualProject: project: VirtualProject -> Task<unit>

  /// <summary>
  /// Inserts a local project into the database.
  /// </summary>
  /// <param name="name">The name of the project.</param>
  /// <param name="configPath">The path to the project configuration file. (migrondi.json)</param>
  /// <param name="description">An optional description of the project.</param>
  /// <returns>The ID of the inserted project.</returns>
  abstract member InsertLocalProject:
    name: string * configPath: string * ?description: string -> Task<Guid>

  abstract member UpdateProject: Project -> Task<unit>

  abstract member UpdateLocalProjectConfigPath:
    id: Guid * path: string -> Task<unit>

let GetRepository createDbConnection =
  let readConfig(path: string) = option {
    try

      let json = File.ReadAllText path

      return!
        JDeck.Decoding.fromString(json, Decoders.migrondiConfigDecoder)
        |> Result.toOption
    with :? FileNotFoundException ->
      return! None
  }

  let findLocalProjects =
    Database.FindLocalProjects(readConfig, createDbConnection)

  let findLocalProjectById =
    Database.FindLocalProjectById(readConfig, createDbConnection)

  let insertLocalProject = Database.InsertLocalProject createDbConnection
  let updateProject = Database.UpdateProject createDbConnection

  let updateLocalProjectConfigPath =
    Database.UpdateLocalProjectConfigPath createDbConnection

  { new IProjectRepository with

      member _.GetProjectById projectId = task {
        let! project = findLocalProjectById projectId

        match project with
        | Some project -> return Some(Local project)
        | None ->
          // TODO: try find virtual project once these are implemented
          return None
      }

      member _.GetProjects() = task {
        let! projects = findLocalProjects()
        // TODO: try find virtual projects once these are implemented
        let virtualProjects = []

        return [
          for project in projects do
            Local project

          for project in virtualProjects do
            Virtual project
        ]
      }

      member _.InsertVirtualProject _ = failwith "Not Implemented"

      member _.InsertLocalProject(name, path, description) =
        insertLocalProject(name, description, path)

      member _.UpdateProject arg1 = task {
        match arg1 with
        | Local project ->
          do! updateProject(project.id, project.name, project.description)
        | Virtual v -> failwith "Not Implemented"
      }

      member _.UpdateLocalProjectConfigPath(id, path) =
        updateLocalProjectConfigPath(id, path)
  }
