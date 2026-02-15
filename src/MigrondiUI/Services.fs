module MigrondiUI.Services

open System
open System.IO
open System.Collections.Concurrent

open Microsoft.Extensions.Logging

open IcedTasks
open FsToolkit.ErrorHandling

open Migrondi.Core
open Migrondi.Core.Serialization
open MigrondiUI

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

type ProjectCollection
  (
    logger: ILogger<ProjectCollection>,
    createConnection: unit -> System.Data.IDbConnection
  ) =

  let readConfig(path: string) =
    try
      let json = File.ReadAllText path
      MiSerializer.DecodeConfig json |> Ok |> Result.toOption
    with :? FileNotFoundException ->
      None

  let findLocalProjects =
    Database.FindLocalProjects(readConfig, createConnection)

  let findLocalProjectById =
    Database.FindLocalProjectById(readConfig, createConnection)

  let insertLocalProject = Database.InsertLocalProject createConnection
  let updateProject = Database.UpdateProject createConnection

  let findVirtualProjects = Database.FindVirtualProjects createConnection
  let findVirtualProjectById = Database.FindVirtualProjectById createConnection
  let insertVirtualProject = Database.InsertVirtualProject createConnection
  let updateVirtualProject = Database.UpdateVirtualProject createConnection

  let findVirtualMigrations =
    Database.FindVirtualMigrationsByProjectId createConnection

  let insertVirtualMigration = Database.InsertVirtualMigration createConnection

  member x.DeleteProject(projectId: Guid, kind: DeleteKind) = cancellableTask {
    let! local = findLocalProjectById projectId

    let! project = cancellableTask {
      match local with
      | Some p -> return Some(Local p)
      | None ->
        let! virtualProj = findVirtualProjectById projectId
        return virtualProj |> Option.map Virtual
    }

    match project with
    | None -> return Error ProjectDeleteError.NotFound
    | Some p ->
      match kind with
      | DeleteKind.Soft ->
        let baseProjectId =
          match p with
          | Local lp -> lp.id
          | Virtual vp -> vp.projectId

        do!
          updateProject {
            id = baseProjectId
            name = ""
            description = None
          }

        return Ok()

      | DeleteKind.Hard -> return Error ProjectDeleteError.HasAppliedMigrations
  }

  interface IProjectCollection with
    member _.List() = cancellableTask {
      let! localProjects = findLocalProjects()
      let! virtualProjects = findVirtualProjects()

      return [
        for p in localProjects -> Local p
        for p in virtualProjects -> Virtual p
      ]
    }

    member _.Get(projectId: Guid) = cancellableTask {
      let! local = findLocalProjectById projectId

      match local with
      | Some p -> return Some(Local p)
      | None ->
        let! virtualProj = findVirtualProjectById projectId
        return virtualProj |> Option.map Virtual
    }

    member _.RegisterLocal(configPath: string, ?name: string) = cancellableTask {
      let projectName =
        match name with
        | Some n -> n
        | None ->
          let dir = Path.GetDirectoryName configPath

          match dir with
          | null -> "unnamed-project"
          | dir -> Path.GetFileName dir |> nonNull

      return!
        insertLocalProject {
          name = projectName
          description = None
          configPath = configPath
        }
    }

    member _.CreateVirtual(args: Database.InsertVirtualProjectArgs) =
      insertVirtualProject args

    member _.UpdateVirtual(project: VirtualProject) = cancellableTask {
      do!
        updateProject {
          id = project.projectId
          name = project.name
          description = project.description
        }

      do!
        updateVirtualProject {
          id = project.id
          connection = project.connection
          tableName = project.tableName
          driver = project.driver.AsString
        }
    }

    member _.Import(configPath: string) = cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      let rootDir = Path.GetDirectoryName(configPath) |> nonNull

      let projectName =
        match Path.GetFileName(rootDir) with
        | null -> "imported-project"
        | name -> name


      logger.LogInformation(
        "Importing project {projectName} from {rootDir}",
        projectName,
        rootDir
      )

      let! configContent = File.ReadAllTextAsync(configPath, ct)

      let config =
        try
          MiSerializer.DecodeConfig configContent
        with ex ->
          failwith $"Invalid config at {configPath}: {ex.Message}"

      let migrationsDir =
        DirectoryInfo(Path.Combine(rootDir, config.migrations))

      let! migrations =
        migrationsDir.EnumerateFiles("*.sql", SearchOption.TopDirectoryOnly)
        |> Seq.map(fun f -> asyncEx {
          let! ct = Async.CancellationToken
          let! content = File.ReadAllTextAsync(f.FullName, ct)
          logger.LogDebug("Found migration {migrationPath}", f.FullName)
          return MiSerializer.Decode(content, f.Name)
        })
        |> Async.Parallel

      logger.LogInformation("Found {count} migrations", migrations.Length)

      let! virtualId =
        insertVirtualProject {
          name = projectName
          description = Some $"Imported from {projectName}"
          connection = config.connection
          tableName = config.tableName
          driver = config.driver.AsString
        }

      for migration in migrations do
        let! _ =
          insertVirtualMigration {
            name = migration.name
            timestamp = migration.timestamp
            upContent = migration.upContent
            downContent = migration.downContent
            virtualProjectId = virtualId
            manualTransaction = migration.manualTransaction
          }

        logger.LogDebug("Created virtual migration {name}", migration.name)

      logger.LogInformation(
        "Created virtual project {projectName} with {count} migrations",
        projectName,
        migrations.Length
      )

      return virtualId
    }

    member _.Export(projectId: Guid, targetPath: string) = cancellableTask {
      let! ct = CancellableTask.getCancellationToken()

      logger.LogInformation(
        "Exporting project {projectId} to {path}",
        projectId,
        targetPath
      )

      let! project = findVirtualProjectById projectId

      match project with
      | None -> return failwith $"Project {projectId} not found"
      | Some p ->

        let! migrations = findVirtualMigrations p.id

        let config = {
          p.ToMigrondiConfig() with
              migrations = "./migrations"
        }

        let projectRoot = Directory.CreateDirectory(targetPath)
        let migrationsDir = projectRoot.CreateSubdirectory("migrations")

        let configPath = Path.Combine(projectRoot.FullName, "migrondi.json")
        let configContent = MiSerializer.Encode config

        do! File.WriteAllTextAsync(configPath, configContent, ct)

        do!
          migrations
          |> List.map(fun (vm: VirtualMigration) -> asyncEx {
            let! ct = Async.CancellationToken
            let content: string = MiSerializer.Encode(vm.ToMigration())

            let migrationPath =
              Path.Combine(
                migrationsDir.FullName,
                $"{vm.timestamp}_{vm.name}.sql"
              )

            do! File.WriteAllTextAsync(migrationPath, content, ct)
          })
          |> Async.Parallel
          |> Async.Ignore

        return projectRoot.FullName
    }

    member x.DeleteProject(projectId: Guid, kind: DeleteKind) =
      x.DeleteProject(projectId, kind)

type MigrationOperations
  (
    core: IMigrondi,
    projectId: Guid,
    createConnection: unit -> System.Data.IDbConnection,
    isVirtual: bool,
    rootDir: string
  ) =

  let findVirtualMigration =
    Database.FindVirtualMigrationByName createConnection

  let updateVirtualMigration = Database.UpdateVirtualMigration createConnection

  let removeVirtualMigration =
    Database.RemoveVirtualMigrationByName createConnection

  interface IMigrationOperations with
    member _.Core = core

    member _.GetMigration(name: string) = cancellableTask {
      if isVirtual then
        let! result = findVirtualMigration projectId name
        return result |> Option.map(fun vm -> vm.ToMigration())
      else
        let files = Directory.GetFiles(rootDir, $"*_{name}.sql")

        match files with
        | [||] -> return None
        | _ ->
          let path = files[0]
          let! ct = CancellableTask.getCancellationToken()
          let! content = File.ReadAllTextAsync(path, ct)
          let name = Path.GetFileName path |> nonNull
          let migration = MiSerializer.Decode(content, name)
          return Some migration
    }

    member _.UpdateMigration(migration: Migration) = cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      do! core.InitializeAsync ct
      let! status = core.MigrationsListAsync ct

      let appliedNames =
        status
        |> Seq.choose (function
          | Applied m -> Some m.name
          | Pending _ -> None)
        |> Set.ofSeq

      if Set.contains migration.name appliedNames then
        return Error(MigrationCrudError.AlreadyApplied migration.name)
      else if isVirtual then
        let! existing = findVirtualMigration projectId migration.name

        match existing with
        | None -> return Error(MigrationCrudError.NotFound migration.name)
        | Some _ ->
          do!
            updateVirtualMigration {
              virtualProjectId = projectId
              name = migration.name
              upContent = migration.upContent
              downContent = migration.downContent
              manualTransaction = migration.manualTransaction
            }

          return Ok()
      else
        let files = Directory.GetFiles(rootDir, $"*_{migration.name}.sql")

        match files with
        | [||] -> return Error(MigrationCrudError.NotFound migration.name)
        | _ ->
          let path = files[0]
          let content = MiSerializer.Encode migration
          do! File.WriteAllTextAsync(path, content, ct)
          return Ok()
    }

    member _.DeleteMigration(name: string) = cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      do! core.InitializeAsync ct
      let! status = core.MigrationsListAsync ct

      let appliedNames =
        status
        |> Seq.choose (function
          | Applied m -> Some m.name
          | Pending _ -> None)
        |> Set.ofSeq

      if Set.contains name appliedNames then
        return Error(MigrationCrudError.AlreadyApplied name)
      else if isVirtual then
        do! removeVirtualMigration projectId name
        return Ok()
      else
        let files = Directory.GetFiles(rootDir, $"*_{name}.sql")

        match files with
        | [||] -> return Error(MigrationCrudError.NotFound name)
        | _ ->
          File.Delete files[0]
          return Ok()
    }

type MigrationOperationsFactory
  (
    loggerFactory: ILoggerFactory,
    createConnection: unit -> System.Data.IDbConnection
  ) =

  let logger = loggerFactory.CreateLogger<MigrationOperationsFactory>()
  let cache = ConcurrentDictionary<Guid, IMigrationOperations>()

  let normalizeSqliteConnection (vProjectId: Guid) (connection: string) =
    let pattern =
      System.Text.RegularExpressions.Regex(
        @"Data Source\s*=\s*(.+?)(?:;|$)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
      )

    let m = pattern.Match(connection)

    if not m.Success then
      connection
    else
      let dataSource = m.Groups[1].Value.Trim()

      if Path.IsPathRooted dataSource then
        connection
      elif dataSource.StartsWith(":") then
        connection
      else
        let appData =
          Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)

        let baseDir = Path.Combine(appData, "MigrondiUI", vProjectId.ToString())
        let fileName = dataSource.Replace("./", "").Replace(".\\", "")
        let absolutePath = Path.Combine(baseDir, fileName)

        if not(Directory.Exists baseDir) then
          Directory.CreateDirectory baseDir |> ignore

        let restOfConnection = connection.Substring(m.Index + m.Length)
        $"Data Source={absolutePath}{restOfConnection}"

  interface IMigrationOperationsFactory with
    member _.Create(project: Project) =
      match project with
      | Local p ->
        match p.config with
        | None -> failwith $"Project {p.name} has no valid config"
        | Some config ->
          let rootDir = Path.GetDirectoryName p.migrondiConfigPath |> nonNull

          cache.GetOrAdd(
            p.id,
            fun _ ->
              let mLogger = loggerFactory.CreateLogger<IMigrondi>()
              let core = Migrondi.MigrondiFactory(config, rootDir, mLogger)

              MigrationOperations(core, p.id, createConnection, false, rootDir)
              :> IMigrationOperations
          )

      | Virtual p ->
        let rootDir = "migrondi-ui://projects/virtual/"

        cache.GetOrAdd(
          p.id,
          fun _ ->
            let normalizedConfig =
              match p.driver with
              | MigrondiDriver.Sqlite -> {
                  p.ToMigrondiConfig() with
                      connection = normalizeSqliteConnection p.id p.connection
                }
              | _ -> p.ToMigrondiConfig()

            let mLogger = loggerFactory.CreateLogger<IMigrondi>()

            let vfs =
              let vLogger = loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>()
              VirtualFs.getVirtualFs(vLogger, createConnection)

            let core =
              Migrondi.MigrondiFactory(normalizedConfig, rootDir, mLogger, vfs)

            MigrationOperations(core, p.id, createConnection, true, rootDir)
            :> IMigrationOperations
        )
