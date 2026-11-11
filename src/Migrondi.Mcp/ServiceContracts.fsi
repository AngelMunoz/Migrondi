namespace MigrondiUI

module Database =

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

    abstract CreateVirtual:
      args: Database.InsertVirtualProjectArgs ->
        IcedTasks.CancellableTasks.CancellableTask<System.Guid>

    abstract DeleteProject:
      projectId: System.Guid * kind: DeleteKind ->
        IcedTasks.CancellableTasks.CancellableTask<
          Result<unit, ProjectDeleteError>
         >

    abstract Export:
      projectId: System.Guid * targetPath: string ->
        IcedTasks.CancellableTasks.CancellableTask<string>

    abstract Get:
      projectId: System.Guid ->
        IcedTasks.CancellableTasks.CancellableTask<Project option>

    abstract Import:
      configPath: string ->
        IcedTasks.CancellableTasks.CancellableTask<System.Guid>

    abstract List:
      unit -> IcedTasks.CancellableTasks.CancellableTask<Project list>

    abstract RegisterLocal:
      configPath: string * ?name: string ->
        IcedTasks.CancellableTasks.CancellableTask<System.Guid>

    abstract UpdateVirtual:
      project: VirtualProject ->
        IcedTasks.CancellableTasks.CancellableTask<unit>

  type IMigrationOperations =

    abstract DeleteMigration:
      name: string ->
        IcedTasks.CancellableTasks.CancellableTask<
          Result<unit, MigrationCrudError>
         >

    abstract GetMigration:
      name: string ->
        IcedTasks.CancellableTasks.CancellableTask<
          Migrondi.Core.Migration option
         >

    abstract UpdateMigration:
      migration: Migrondi.Core.Migration ->
        IcedTasks.CancellableTasks.CancellableTask<
          Result<unit, MigrationCrudError>
         >

    abstract Core: Migrondi.Core.IMigrondi

  type IMigrationOperationsFactory =

    abstract Create: project: Project -> IMigrationOperations
