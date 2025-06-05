module MigrondiUI.VirtualFs

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open System.Threading
open IcedTasks

open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization

open MigrondiUI.Projects
open Microsoft.Extensions.Logging
open FsToolkit.ErrorHandling

[<Literal>]
let MigrationNameSchema: string = "^(?<Timestamp>[0-9]+)_(?<Name>.+).(sql|SQL)$"

[<return: Struct>]
let (|HasGroup|_|) (name: string) (groups: Match) =
  if not groups.Success then
    ValueNone
  else
    match groups.Groups[name] with
    | group when group.Length = 0 -> ValueNone
    | group -> ValueSome group.Value

let mnRegex = lazy Regex(MigrationNameSchema)

let extractTimestampAndName(name: string) =
  let groups = mnRegex.Value.Match(name)

  match groups with
  | HasGroup "Timestamp" timestamp & HasGroup "Name" name ->
    let timestamp = Int64.Parse timestamp
    let name = name.Trim()

    if String.IsNullOrWhiteSpace name then
      failwith $"Invalid migration name %s{name}"

    struct (timestamp, name)
  | _ -> failwith $"Invalid migration name %s{name}"

type MigrondiUIFs =
  inherit IMiFileSystem

  abstract member ExportToLocal:
    project: Guid * projectPath: string -> CancellableTask<string>

  abstract member ImportFromLocal:
    project: LocalProject -> CancellableTask<Guid>

  abstract member ImportFromLocal: projectPath: string -> CancellableTask<Guid>

let getVirtualFs
  (logger: ILogger<MigrondiUIFs>, vpr: IVirtualProjectRepository)
  =
  let importProjectFromPath (configPath: string) (projectName: string) = cancellableTask {
    let configSerializer, migrationSerializer =
      let serializer = MigrondiSerializer()

      serializer :> IMiConfigurationSerializer,
      serializer :> IMiMigrationSerializer

    let rootDir = Path.GetDirectoryName(configPath)

    logger.LogInformation(
      "Importing project {projectName} from {rootDir}",
      projectName,
      rootDir

    )

    let! config = cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let! content = File.ReadAllTextAsync(configPath, token)
      return configSerializer.Decode content
    }

    logger.LogInformation("Found config {config}", config)

    let migrationsDir =
      DirectoryInfo(Path.Combine(nonNull rootDir, config.migrations))

    let! migrations =
      migrationsDir.EnumerateFiles("*.sql", SearchOption.TopDirectoryOnly)
      |> Seq.map(fun f -> asyncEx {
        let! token = Async.CancellationToken
        let! content = File.ReadAllTextAsync(f.FullName, token)
        logger.LogDebug("Found migration {migrationPath}", f.FullName)
        return migrationSerializer.DecodeText content
      })
      |> Async.Parallel

    logger.LogInformation("Found migrations {migrations}", migrations.Length)

    logger.LogInformation(
      "Creating new virtual project {projectName}",
      projectName
    )

    let! vProjectId =
      vpr.InsertProject {
        name = projectName
        description = $"Imported from local ({projectName})"
        connection = config.connection
        tableName = config.tableName
        driver = config.driver
      }

    let! migrations =
      migrations
      |> Array.map(fun migration -> asyncEx {
        let! token = Async.CancellationToken

        let vMigration = {
          id = Guid.NewGuid()
          name = migration.name
          timestamp = migration.timestamp
          upContent = migration.upContent
          downContent = migration.downContent
          manualTransaction = migration.manualTransaction
          projectId = vProjectId
        }

        logger.LogDebug(
          "Creating virtual migration {vMigration}",
          vMigration.name
        )

        return! vpr.InsertMigration vMigration token

      })
      |> Async.Parallel

    logger.LogInformation(
      "Created virtual project {projectName} with {migrations} migrations",
      projectName,
      migrations.Length
    )

    return vProjectId
  }

  { new MigrondiUIFs with

      member _.ListMigrations migrationsLocation = failwith "Not Implemented"
      member _.ReadConfiguration readFrom = failwith "Not Implemented"
      member _.ReadMigration migrationName = failwith "Not Implemented"

      member _.WriteConfiguration(config, writeTo) = failwith "Not Implemented"

      member _.WriteMigration(migration, migrationName) =
        failwith "Not Implemented"

      member _.ListMigrationsAsync(migrationsLocation, ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None

        logger.LogDebug(
          "Listing migrations for {migrationsLocation}",
          migrationsLocation
        )

        let guid = Guid.Parse migrationsLocation
        let! migrations = vpr.GetMigrations guid ct

        let migrations: Migration list =
          migrations
          |> List.map(fun m -> {
            name = m.name
            timestamp = m.timestamp
            upContent = m.upContent
            downContent = m.downContent
            manualTransaction = m.manualTransaction
          })

        logger.LogDebug(
          "Found {count} migrations for {migrationsLocation}",
          migrations.Length,
          migrationsLocation
        )

        return migrations :> IReadOnlyList<Migration>
      }

      member _.ReadConfigurationAsync(readFrom, ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None
        logger.LogDebug("Reading configuration for {readFrom}", readFrom)
        let guid = Guid.Parse readFrom
        let! config = vpr.GetProjectById guid ct

        match config with
        | None -> return failwith $"Project with id %s{readFrom} not found"
        | Some config ->
          return {
            connection = config.connection
            migrations = config.id.ToString()
            tableName = config.tableName
            driver = config.driver
          }
      }

      member _.ReadMigrationAsync(migrationName, ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None

        logger.LogDebug("Reading migration for {migrationName}", migrationName)

        let! migration = vpr.GetMigrationByName migrationName ct

        match migration with
        | None ->
          return failwith $"Migration with name %s{migrationName} not found"
        | Some migration ->
          logger.LogDebug(
            "Found migration with name {migrationName}",
            migrationName
          )

          return {
            name = migration.name
            timestamp = migration.timestamp
            upContent = migration.upContent
            downContent = migration.downContent
            manualTransaction = migration.manualTransaction
          }
      }

      member _.WriteConfigurationAsync(config, writeTo, ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None
        logger.LogDebug("Writing configuration for {writeTo}", writeTo)
        let guid = Guid.Parse writeTo
        let! project = vpr.GetProjectById guid ct

        match project with
        | None -> return failwith $"Project with id %s{writeTo} not found"
        | Some project ->
          let updatedProject = {
            project with
                connection = config.connection
                tableName = config.tableName
                driver = config.driver
          }

          logger.LogDebug("Found project with id {writeTo}", writeTo)
          return! vpr.UpdateProject updatedProject ct
      }

      member _.WriteMigrationAsync
        (migration, migrationName, ?cancellationToken)
        =
        task {
          let ct = defaultArg cancellationToken CancellationToken.None

          let migrationName, projectId =
            match migrationName.Split '~' with
            | [| name; projectId |] ->
              $"{name}.sql", Guid.Parse(projectId.Remove 36)
            | _ -> failwith "Invalid migration name format"


          logger.LogDebug(
            "Writing migration for {migrationName}",
            migrationName
          )

          let struct (timestamp, name) = extractTimestampAndName migrationName

          logger.LogDebug(
            "Extracted timestamp {timestamp} and name {name} from {migrationName}",
            timestamp,
            name,
            migrationName
          )

          logger.LogDebug("Looking for project with id {guid}", projectId)

          let virtualMigration: VirtualMigration = {
            id = Guid.NewGuid()
            name = name
            timestamp = timestamp
            upContent = migration.upContent
            downContent = migration.downContent
            projectId = projectId
            manualTransaction = migration.manualTransaction
          }

          return! vpr.InsertMigration virtualMigration ct
        }

      member _.ExportToLocal(project, path) = cancellableTask {
        let! token = CancellableTask.getCancellationToken()

        logger.LogInformation(
          "Exporting project {project} to {path}",
          project,
          path
        )

        let! found = taskOption {
          let! project = vpr.GetProjectById project token
          let! migrations = vpr.GetMigrations project.id token
          return project, migrations
        }

        match found with
        | None ->
          logger.LogWarning("Project with id {project} not found", project)
          return failwith $"Project with id {project} not found"
        | Some(project, migrations) ->
          logger.LogInformation(
            "Found project {project} with {migrations} migrations",
            project,
            migrations.Length
          )

          let config = {
            project.ToMigrondiConfig() with
                migrations = "./migrations"
          }

          let configSerializer, migrationSerializer =
            let serializer = MigrondiSerializer()

            serializer :> IMiConfigurationSerializer,
            serializer :> IMiMigrationSerializer

          let projectRoot = Directory.CreateDirectory(path)
          let migrationsDir = projectRoot.CreateSubdirectory("migrations")
          projectRoot.Create()
          migrationsDir.Create()

          logger.LogDebug(
            "Created project root {projectRoot} and migrations dir {migrationsDir}",
            projectRoot.FullName,
            migrationsDir.FullName
          )

          let configPath = Path.Combine(projectRoot.FullName, "migrondi.json")
          let configContent = configSerializer.Encode config

          logger.LogInformation("Writing config to {configPath}", configPath)

          do! File.WriteAllTextAsync(configPath, configContent, token)

          logger.LogInformation(
            "Writing migrations to {migrationsDir}",
            migrationsDir.FullName
          )

          do!
            migrations
            |> List.map(fun vm -> asyncEx {
              let! token = Async.CancellationToken

              let migration = vm.ToMigration()
              let content = migrationSerializer.EncodeText migration

              let migrationPath =
                Path.Combine(
                  migrationsDir.FullName,
                  $"{vm.timestamp}_{vm.name}.sql"
                )

              logger.LogDebug(
                "Writing migration {migrationPath}",
                migrationPath
              )

              do! File.WriteAllTextAsync(migrationPath, content, token)
              return ()
            })
            |> Async.Parallel
            |> Async.Ignore

          logger.LogInformation(
            "Project exported to {projectRoot}",
            projectRoot.FullName
          )

          return projectRoot.FullName
      }

      member _.ImportFromLocal(project: LocalProject) : CancellableTask<Guid> =
        importProjectFromPath project.migrondiConfigPath project.name

      member _.ImportFromLocal(projectConfigPath: string) =

        let projectName = Path.GetDirectoryName projectConfigPath |> nonNull

        importProjectFromPath projectConfigPath projectName
  }
