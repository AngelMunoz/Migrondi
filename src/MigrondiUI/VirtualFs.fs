module MigrondiUI.VirtualFs

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks
open IcedTasks

open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization

open MigrondiUI.Projects
open Microsoft.Extensions.Logging

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

  abstract member ExportToLocal: project: Guid * projectPath: string -> CancellableTask<string>

  abstract member ImportFromLocal:
    project: LocalProject -> CancellableTask<Guid>

  abstract member ImportFromLocal: projectPath: string -> CancellableTask<Guid>

let getVirtualFs
  (logger: ILogger<MigrondiUIFs>, vpr: IVirtualProjectRepository)
  =
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

      member _.ExportToLocal (project, path) = failwith "Not Implemented"

      member _.ImportFromLocal(project: LocalProject) : CancellableTask<Guid> =
        failwith "Not Implemented"

      member _.ImportFromLocal(projectPath: string) : CancellableTask<Guid> =
        failwith "Not Implemented"
  }
