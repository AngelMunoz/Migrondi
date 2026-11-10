namespace MigrondiUI

open System
open IcedTasks
open Migrondi.Core

module Database =
  // Single definition of these args lives here so the service contracts
  // below can reference them without a circular assembly reference.
  [<Struct>]
  type InsertVirtualProjectArgs = {
    name: string
    description: string option
    connection: string
    tableName: string
    driver: string
  }

module Services =

  [<RequireQualifiedAccess>]
  type DeleteKind =
    | Soft
    | Hard

  [<RequireQualifiedAccess>]
  type MigrationCrudError =
    | NotFound of name: string
    | AlreadyApplied of name: string
    | DatabaseError of message: string

  [<RequireQualifiedAccess>]
  type ProjectDeleteError =
    | NotFound
    | HasAppliedMigrations

  type IProjectCollection =

    abstract List: unit -> CancellableTask<Project list>
    abstract Get: projectId: Guid -> CancellableTask<Project option>

    abstract RegisterLocal:
      configPath: string * ?name: string -> CancellableTask<Guid>

    abstract CreateVirtual:
      args: Database.InsertVirtualProjectArgs -> CancellableTask<Guid>

    abstract UpdateVirtual: project: VirtualProject -> CancellableTask<unit>

    abstract Import: configPath: string -> CancellableTask<Guid>

    abstract Export:
      projectId: Guid * targetPath: string -> CancellableTask<string>

    abstract DeleteProject:
      projectId: Guid * kind: DeleteKind ->
        CancellableTask<Result<unit, ProjectDeleteError>>

  type IMigrationOperations =
    abstract Core: IMigrondi
    abstract GetMigration: name: string -> CancellableTask<Migration option>

    abstract UpdateMigration:
      migration: Migration -> CancellableTask<Result<unit, MigrationCrudError>>

    abstract DeleteMigration:
      name: string -> CancellableTask<Result<unit, MigrationCrudError>>

  type IMigrationOperationsFactory =
    abstract Create: project: Project -> IMigrationOperations
