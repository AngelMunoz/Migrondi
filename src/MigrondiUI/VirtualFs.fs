module MigrondiUI.VirtualFs

open System
open System.IO
open System.Data
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

open IcedTasks
open Donald

open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization

open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging

type MigrondiUIFs =
  inherit IMiMigrationSource

type VirtualProjectResource =
  | ProjectConfig of projectId: Guid
  | Migration of projectId: Guid * migrationName: string
  | MigrationList of projectId: Guid

let private parseVirtualProjectUri(uri: Uri) : VirtualProjectResource option =
  if uri.Scheme <> "migrondi-ui" then
    None
  else
    let segments = uri.Segments |> Array.map(fun s -> s.TrimEnd('/'))

    match segments with
    | [| ""; "virtual"; projectId; "config" |] ->
      match Guid.TryParse projectId with
      | true, id -> Some(ProjectConfig id)
      | _ -> None

    | [| ""; "virtual"; projectId |] ->
      match Guid.TryParse projectId with
      | true, id -> Some(MigrationList id)
      | _ -> None

    | [| ""; "virtual"; projectId; migrationName |] ->
      match Guid.TryParse projectId with
      | true, id -> Some(Migration(id, migrationName))
      | _ -> None

    | _ -> None

let getVirtualFs
  (logger: ILogger<MigrondiUIFs>, createConnection: unit -> IDbConnection)
  : MigrondiUIFs =

  let mapVirtualProject(r: IDataReader) =
    let id = r.ReadGuid "id"
    let name = r.ReadString "name"
    let description = r.ReadStringOption "description"
    let connection = r.ReadString "connection"
    let tableName = r.ReadString "table_name"
    let driverStr = r.ReadString "driver"
    let projectId = r.ReadGuid "project_id"

    {
      id = id
      name = name
      description = description
      connection = connection
      tableName = tableName
      driver = MigrondiDriver.FromString driverStr
      projectId = projectId
    }

  let mapMigration(r: IDataReader) =
    let name = r.ReadString "name"
    let timestamp = r.ReadInt64 "timestamp"
    let upContent = r.ReadString "up_content"
    let downContent = r.ReadString "down_content"
    let manualTransaction = r.ReadBoolean "manual_transaction"

    {
      name = name
      timestamp = timestamp
      upContent = upContent
      downContent = downContent
      manualTransaction = manualTransaction
    }

  { new MigrondiUIFs with
      member _.ReadContent(uri: Uri) =
        failwith
          "Synchronous read is not supported. Use ReadContentAsync instead."

      member _.ReadContentAsync(uri, [<Optional>] ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None

        logger.LogDebug("Reading content for {uri}", uri)

        match parseVirtualProjectUri uri with
        | Some(ProjectConfig projectId) ->
          use conn = createConnection()
          do! conn.TryOpenConnectionAsync(ct)

          let! project =
            conn
            |> Db.newCommand
              "
              SELECT vp.id, p.name, p.description, vp.connection, vp.table_name, vp.driver, vp.project_id
              FROM virtual_projects vp
              INNER JOIN projects p ON vp.project_id = p.id
              WHERE vp.id = @id AND p.deleted_at IS NULL
              "
            |> Db.setCancellationToken ct
            |> Db.setParams [ "id", sqlString projectId ]
            |> Db.Async.querySingle mapVirtualProject

          match project with
          | None -> return failwith $"Project {projectId} not found"
          | Some p ->
            let config: MigrondiConfig = {
              connection = p.connection
              migrations = p.id.ToString()
              tableName = p.tableName
              driver = p.driver
            }

            return MiSerializer.Encode config

        | Some(Migration(projectId, migrationFileName)) ->
          let migrationName =
            match Migration.ExtractFromFilename migrationFileName with
            | Ok(name, _) -> name
            | Error _ -> migrationFileName.Replace(".sql", "")

          use conn = createConnection()
          do! conn.TryOpenConnectionAsync(ct)

          let! migration =
            conn
            |> Db.newCommand
              "
              SELECT vm.name, vm.timestamp, vm.up_content, vm.down_content, vm.manual_transaction
              FROM virtual_migrations vm
              INNER JOIN virtual_projects vp ON vm.virtual_project_id = vp.id
              WHERE vp.id = @projectId AND vm.name = @name
              "
            |> Db.setCancellationToken ct
            |> Db.setParams [
              "projectId", sqlString projectId
              "name", sqlString migrationName
            ]
            |> Db.Async.querySingle mapMigration

          match migration with
          | None -> return failwith $"Migration {migrationName} not found"
          | Some m -> return MiSerializer.Encode m

        | Some(MigrationList _) ->
          return failwith "Cannot read content from migration list URI"

        | None -> return failwith $"Unsupported URI: {uri}"
      }

      member _.WriteContent(uri, content) =
        failwith
          "Synchronous write is not supported. Use WriteContentAsync instead."

      member _.WriteContentAsync
        (uri, content, [<Optional>] ?cancellationToken)
        =
        task {
          let ct = defaultArg cancellationToken CancellationToken.None

          logger.LogDebug("Writing content to {uri}", uri)

          match parseVirtualProjectUri uri with
          | Some(ProjectConfig projectId) ->
            use conn = createConnection()
            do! conn.TryOpenConnectionAsync(ct)

            let! project =
              conn
              |> Db.newCommand
                "
              SELECT vp.id, p.name, p.description, vp.connection, vp.table_name, vp.driver, vp.project_id
              FROM virtual_projects vp
              INNER JOIN projects p ON vp.project_id = p.id
              WHERE vp.id = @id AND p.deleted_at IS NULL
              "
              |> Db.setCancellationToken ct
              |> Db.setParams [ "id", sqlString projectId ]
              |> Db.Async.querySingle mapVirtualProject

            match project with
            | None -> return failwith $"Project {projectId} not found"
            | Some p ->
              let config = MiSerializer.DecodeConfig content

              do!
                conn
                |> Db.newCommand
                  "
                UPDATE projects SET updated_at = @now WHERE id = @projectId
                "
                |> Db.setCancellationToken ct
                |> Db.setParams [
                  "projectId", sqlString p.projectId
                  "now", sqlString DateTime.UtcNow
                ]
                |> Db.Async.exec

              do!
                conn
                |> Db.newCommand
                  "
                UPDATE virtual_projects
                SET connection = @connection, table_name = @tableName, driver = @driver
                WHERE id = @id
                "
                |> Db.setCancellationToken ct
                |> Db.setParams [
                  "id", sqlString projectId
                  "connection", sqlString config.connection
                  "tableName", sqlString config.tableName
                  "driver", sqlString config.driver.AsString
                ]
                |> Db.Async.exec

          | Some(Migration(projectId, migrationFileName)) ->
            let migrationName =
              match Migration.ExtractFromFilename migrationFileName with
              | Ok(name, _) -> name
              | Error _ -> migrationFileName.Replace(".sql", "")

            let migration = MiSerializer.Decode(content, migrationName)

            use conn = createConnection()
            do! conn.TryOpenConnectionAsync(ct)

            let! existingId =
              conn
              |> Db.newCommand
                "
              SELECT vm.id
              FROM virtual_migrations vm
              INNER JOIN virtual_projects vp ON vm.virtual_project_id = vp.id
              WHERE vp.id = @projectId AND vm.name = @name
              "
              |> Db.setCancellationToken ct
              |> Db.setParams [
                "projectId", sqlString projectId
                "name", sqlString migrationName
              ]
              |> Db.Async.querySingle(fun r -> r.ReadGuid "id")

            match existingId with
            | Some existingId ->
              do!
                conn
                |> Db.newCommand
                  "
                UPDATE virtual_migrations
                SET timestamp = @timestamp, up_content = @upContent, down_content = @downContent, manual_transaction = @manualTransaction
                WHERE id = @id
                "
                |> Db.setCancellationToken ct
                |> Db.setParams [
                  "id", sqlString existingId
                  "timestamp", sqlInt64 migration.timestamp
                  "upContent", sqlString migration.upContent
                  "downContent", sqlString migration.downContent
                  "manualTransaction", sqlBoolean migration.manualTransaction
                ]
                |> Db.Async.exec

            | None ->
              do!
                conn
                |> Db.newCommand
                  "
                INSERT INTO virtual_migrations (id, name, timestamp, up_content, down_content, manual_transaction, virtual_project_id)
                SELECT @id, @name, @timestamp, @upContent, @downContent, @manualTransaction, vp.id
                FROM virtual_projects vp
                WHERE vp.id = @projectId
                "
                |> Db.setCancellationToken ct
                |> Db.setParams [
                  "id", sqlString(Guid.NewGuid())
                  "name", sqlString migration.name
                  "timestamp", sqlInt64 migration.timestamp
                  "upContent", sqlString migration.upContent
                  "downContent", sqlString migration.downContent
                  "manualTransaction", sqlBoolean migration.manualTransaction
                  "projectId", sqlString projectId
                ]
                |> Db.Async.exec

          | Some(MigrationList _) ->
            return failwith "Cannot write content to migration list URI"

          | None -> return failwith $"Unsupported URI: {uri}"
        }

      member _.ListFiles(locationUri) =
        failwith
          "Synchronous listing is not supported. Use ListFilesAsync instead."

      member _.ListFilesAsync(locationUri, [<Optional>] ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None

        logger.LogDebug("Listing files in {uri}", locationUri)

        match parseVirtualProjectUri locationUri with
        | Some(MigrationList projectId) ->
          use conn = createConnection()
          do! conn.TryOpenConnectionAsync(ct)

          let! migrations =
            conn
            |> Db.newCommand
              "
              SELECT vm.timestamp, vm.name
              FROM virtual_migrations vm
              INNER JOIN virtual_projects vp ON vm.virtual_project_id = vp.id
              WHERE vp.id = @projectId
              ORDER BY vm.timestamp
              "
            |> Db.setCancellationToken ct
            |> Db.setParams [ "projectId", sqlString projectId ]
            |> Db.Async.query(fun r ->
              let timestamp = r.ReadInt64 "timestamp"
              let name = r.ReadString "name"
              timestamp, name)

          let basePath = locationUri.ToString().TrimEnd('/') + "/"

          return
            migrations
            |> List.map(fun (timestamp, name) ->
              Uri($"{basePath}{timestamp}_{name}.sql"))
            :> Uri seq

        | Some(ProjectConfig _) ->
          return failwith "Cannot list files from config URI"

        | Some(Migration _) ->
          return failwith "Cannot list files from single migration URI"

        | None -> return failwith $"Unsupported URI: {locationUri}"
      }
  }
