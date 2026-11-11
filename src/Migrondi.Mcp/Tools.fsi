namespace Migrondi.Mcp

module McpTools =

  val listProjects:
    env: McpEnvironment ->
      IcedTasks.CancellableTasks.CancellableTask<MigrondiUI.Project list>

  val getProject:
    env: McpEnvironment ->
    projectId: System.Guid ->
      IcedTasks.CancellableTasks.CancellableTask<MigrondiUI.Project option>

  val listMigrations:
    env: McpEnvironment ->
    projectId: System.Guid ->
      IcedTasks.CancellableTasks.CancellableTask<
        Migrondi.Core.MigrationStatus list
       >

  type GetMigrationError =
    | ProjectNotFound
    | LocalProjectsNotSupported
    | MigrationNotFound

  val getMigration:
    env: McpEnvironment ->
    projectId: System.Guid ->
    migrationName: string ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<MigrondiUI.VirtualMigration, GetMigrationError>
       >

  val dryRunMigrations:
    env: McpEnvironment ->
    projectId: System.Guid ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<Migrondi.Core.Migration list>

  val dryRunRollback:
    env: McpEnvironment ->
    projectId: System.Guid ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<Migrondi.Core.Migration list>

  type RunMigrationsError =
    | ProjectNotFound
    | ExecutionFailed of string

  val runMigrations:
    env: McpEnvironment ->
    projectId: System.Guid ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<Migrondi.Core.MigrationRecord list, RunMigrationsError>
       >

  type RunRollbackError =
    | ProjectNotFound
    | ExecutionFailed of string

  val runRollback:
    env: McpEnvironment ->
    projectId: System.Guid ->
    amount: int option ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<Migrondi.Core.MigrationRecord list, RunRollbackError>
       >

  type CreateMigrationError =
    | ProjectNotFound
    | InvalidMigrationName of string
    | CreationFailed of string

  val createMigration:
    env: McpEnvironment ->
    projectId: System.Guid ->
    name: string ->
    upContent: string option ->
    downContent: string option ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<
          {|
            fullName: string
            name: string
            timestamp: int64
          |},
          CreateMigrationError
         >
       >

  type UpdateMigrationError =
    | ProjectNotFound
    | MigrationNotFound
    | AlreadyApplied
    | DatabaseError of string

  val updateMigration:
    env: McpEnvironment ->
    guid: System.Guid ->
    name: string ->
    upContent: string ->
    downContent: string ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<unit, UpdateMigrationError>
       >

  type DeleteMigrationError =
    | ProjectNotFound
    | MigrationNotFound
    | AlreadyApplied
    | DatabaseError of string

  val deleteMigration:
    env: McpEnvironment ->
    guid: System.Guid ->
    name: string ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<unit, DeleteMigrationError>
       >

  type CreateProjectError = CreationFailed of string

  val createVirtualProject:
    env: McpEnvironment ->
    name: string ->
    connection: string ->
    driver: Migrondi.Core.MigrondiDriver ->
    description: string option ->
    tableName: string option ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<System.Guid, CreateProjectError>
       >

  type UpdateProjectError =
    | ProjectNotFound
    | LocalProjectsNotSupported
    | UpdateFailed of string

  val updateVirtualProject:
    env: McpEnvironment ->
    projectId: System.Guid ->
    name: string option ->
    connection: string option ->
    tableName: string option ->
    driver: Migrondi.Core.MigrondiDriver option ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<unit, UpdateProjectError>
       >

  type DeleteProjectError =
    | ProjectNotFound
    | HasAppliedMigrations

  val deleteProject:
    env: McpEnvironment ->
    projectId: System.Guid ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<unit, DeleteProjectError>
       >

  type ExportProjectError =
    | ProjectNotFound
    | LocalProjectsNotSupported
    | ExportFailed of string

  val exportVirtualProject:
    env: McpEnvironment ->
    projectId: System.Guid ->
    exportPath: string ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<string, ExportProjectError>
       >

  type ImportProjectError = ImportFailed of string

  val importFromLocal:
    env: McpEnvironment ->
    configPath: string ->
      IcedTasks.CancellableTasks.CancellableTask<
        Result<System.Guid, ImportProjectError>
       >
