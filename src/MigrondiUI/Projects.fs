module MigrondiUI.Projects

open System
open System.IO

open FsToolkit.ErrorHandling
open Migrondi.Core
open Migrondi.Core.Serialization

open IcedTasks
open MigrondiUI.Database

type NewVirtualProjectArgs = {
  name: string
  description: string
  connection: string
  tableName: string
  driver: MigrondiDriver
}

type IVirtualProjectRepository =
  abstract member GetProjects: unit -> CancellableTask<VirtualProject list>
  abstract member GetProjectById: Guid -> CancellableTask<VirtualProject option>

  abstract member InsertProject: NewVirtualProjectArgs -> CancellableTask<Guid>
  abstract member UpdateProject: VirtualProject -> CancellableTask<unit>

  abstract member InsertMigration: VirtualMigration -> CancellableTask<Guid>
  abstract member UpdateMigration: VirtualMigration -> CancellableTask<unit>
  abstract member RemoveMigrationByName: string -> CancellableTask<unit>

  abstract member GetMigrations: Guid -> CancellableTask<VirtualMigration list>


  abstract member GetMigrationByName:
    string -> CancellableTask<VirtualMigration option>

type ILocalProjectRepository =
  abstract member GetProjects: unit -> CancellableTask<LocalProject list>
  abstract member GetProjectById: Guid -> CancellableTask<LocalProject option>

  /// <summary>
  /// Inserts a local project into the database.
  /// </summary>
  /// <param name="name">The name of the project.</param>
  /// <param name="configPath">The path to the project configuration file. (migrondi.json)</param>
  /// <param name="description">An optional description of the project.</param>
  /// <returns>The ID of the inserted project.</returns>
  abstract member InsertProject:
    name: string * configPath: string * ?description: string ->
      CancellableTask<Guid>

  abstract member UpdateProject: LocalProject -> CancellableTask<unit>

  abstract member UpdateProjectConfigPath:
    id: Guid * path: string -> CancellableTask<unit>

let GetLocalProjectRepository createDbConnection =
  let readConfig(path: string) = option {
    try

      let json = File.ReadAllText path

      return!
        MiSerializer.DecodeConfig json
        |> Ok
        |> Result.toOption
    with :? FileNotFoundException ->
      return! None
  }

  let findLocalProjects = FindLocalProjects(readConfig, createDbConnection)

  let findLocalProjectById =
    FindLocalProjectById(readConfig, createDbConnection)

  let insertLocalProject = InsertLocalProject createDbConnection
  let updateProject = UpdateProject createDbConnection

  let updateLocalProjectConfigPath =
    UpdateLocalProjectConfigPath createDbConnection

  { new ILocalProjectRepository with

      member _.GetProjectById projectId = findLocalProjectById projectId

      member _.GetProjects() = findLocalProjects()

      member _.InsertProject(name, path, description) =
        insertLocalProject {
          name = name
          description = description
          configPath = path
        }

      member _.UpdateProject project =
        updateProject {
          id = project.id
          name = project.name
          description = project.description
        }

      member _.UpdateProjectConfigPath(id, path) =
        updateLocalProjectConfigPath(id, path)
  }

let GetVirtualProjectRepository createDbConnection =
  let findVirtualProjects = FindVirtualProjects createDbConnection

  let findVirtualProjectById = FindVirtualProjectById createDbConnection

  let insertVirtualProject = InsertVirtualProject createDbConnection
  let updateVirtualProject = UpdateVirtualProject createDbConnection
  let updateProject = UpdateProject createDbConnection

  let findVirtualMigrationByName = FindVirtualMigrationByName createDbConnection

  let findVirtualMigrationsByProjectId =
    FindVirtualMigrationsByProjectId createDbConnection

  let insertVirtualMigration = InsertVirtualMigration createDbConnection

  let updateVirtualMigration = UpdateVirtualMigration createDbConnection

  let removeVirtualMigrationByName =
    RemoveVirtualMigrationByName createDbConnection

  { new IVirtualProjectRepository with
      member _.GetProjects() = findVirtualProjects()

      member _.GetProjectById projectId = findVirtualProjectById projectId

      member _.InsertProject project =
        insertVirtualProject {
          name = project.name
          description = project.description |> Some
          connection = project.connection
          tableName = project.tableName
          driver = project.driver.AsString
        }

      member _.UpdateProject project = cancellableTask {
        // First update the base project information
        do!
          updateProject {
            id = project.projectId
            name = project.name
            description = project.description
          }

        return!
          updateVirtualProject {
            id = project.id
            connection = project.connection
            tableName = project.tableName
            driver = project.driver.AsString
          }
      }

      member _.InsertMigration migration =
        insertVirtualMigration {
          name = migration.name
          timestamp = migration.timestamp
          upContent = migration.upContent
          downContent = migration.downContent
          virtualProjectId = migration.projectId
          manualTransaction = migration.manualTransaction
        }

      member _.UpdateMigration migration =
        updateVirtualMigration {
          name = migration.name
          upContent = migration.upContent
          downContent = migration.downContent
          manualTransaction = migration.manualTransaction
        }

      member _.RemoveMigrationByName migrationName =
        removeVirtualMigrationByName migrationName

      member _.GetMigrations projectId =
        findVirtualMigrationsByProjectId projectId

      member _.GetMigrationByName name = findVirtualMigrationByName name
  }

let inline GetRepositories createDbConnection =
  GetLocalProjectRepository createDbConnection,
  GetVirtualProjectRepository createDbConnection
