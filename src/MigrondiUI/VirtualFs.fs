module MigrondiUI.VirtualFs

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices
open IcedTasks

open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization

open FsToolkit.ErrorHandling

open MigrondiUI.Projects
open Microsoft.Extensions.Logging

type MigrondiUIFs =
  inherit IMiMigrationSource

  abstract member ExportToLocal:
    project: Guid * projectPath: string -> CancellableTask<string>

  abstract member ImportFromLocal:
    project: LocalProject -> CancellableTask<Guid>

  abstract member ImportFromLocal: projectPath: string -> CancellableTask<Guid>

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
  (logger: ILogger<MigrondiUIFs>, vpr: IVirtualProjectRepository)
  =
  let importProjectFromPath (configPath: string) (projectName: string) = cancellableTask {
    let rootDir = Path.GetDirectoryName(configPath)

    logger.LogInformation(
      "Importing project {projectName} from {rootDir}",
      projectName,
      rootDir
    )

    let! config = cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let! content = File.ReadAllTextAsync(configPath, token)

      return MiSerializer.DecodeConfig content
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

        return MiSerializer.Decode(content, f.Name)
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
      member this.ReadContent(uri: Uri) =
        failwith
          "Synchronous read is not supported. Use ReadContentAsync instead."

      member this.ReadContentAsync(uri, [<Optional>] ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None

        logger.LogDebug("Reading content for {uri}", uri)

        match parseVirtualProjectUri uri with
        | Some(ProjectConfig projectId) ->
          let! project = vpr.GetProjectById projectId ct

          match project with
          | None -> return failwith $"Project {projectId} not found"
          | Some p ->
            let config = p.ToMigrondiConfig()
            return MiSerializer.Encode config

        | Some(Migration(projectId, migrationFileName)) ->
          let migrationName =
            match Migration.ExtractFromFilename migrationFileName with
            | Ok(name, _) -> name
            | Error _ -> migrationFileName.Replace(".sql", "")

          let! migration = vpr.GetMigrationByName projectId migrationName ct

          match migration with
          | None -> return failwith $"Migration {migrationName} not found"
          | Some m -> return MiSerializer.Encode(m.ToMigration())

        | Some(MigrationList _) ->
          return failwith "Cannot read content from migration list URI"

        | None -> return failwith $"Unsupported URI: {uri}"
      }

      member this.WriteContent(uri, content) =
        failwith
          "Synchronous write is not supported. Use WriteContentAsync instead."

      member this.WriteContentAsync
        (uri, content, [<Optional>] ?cancellationToken)
        =
        task {
          let ct = defaultArg cancellationToken CancellationToken.None

          logger.LogDebug("Writing content to {uri}", uri)

          match parseVirtualProjectUri uri with
          | Some(ProjectConfig projectId) ->
            let! project = vpr.GetProjectById projectId ct

            match project with
            | None -> return failwith $"Project {projectId} not found"
            | Some p ->
              let config = MiSerializer.DecodeConfig content

              let updatedProject = {
                p with
                    connection = config.connection
                    tableName = config.tableName
                    driver = config.driver
              }

              return! vpr.UpdateProject updatedProject ct

          | Some(Migration(projectId, migrationFileName)) ->
            let migrationName =
              match Migration.ExtractFromFilename migrationFileName with
              | Ok(name, _) -> name
              | Error _ -> migrationFileName.Replace(".sql", "")

            let migration = MiSerializer.Decode(content, migrationName)

            let virtualMigration: VirtualMigration = {
              id = Guid.NewGuid()
              name = migration.name
              timestamp = migration.timestamp
              upContent = migration.upContent
              downContent = migration.downContent
              projectId = projectId
              manualTransaction = migration.manualTransaction
            }

            let! existing = vpr.GetMigrationByName projectId migrationName ct

            match existing with
            | Some _ -> return! vpr.UpdateMigration virtualMigration ct
            | None ->
              let! _ = vpr.InsertMigration virtualMigration ct
              return ()

          | Some(MigrationList _) ->
            return failwith "Cannot write content to migration list URI"

          | None -> return failwith $"Unsupported URI: {uri}"
        }

      member this.ListFiles(locationUri) =
        failwith
          "Synchronous listing is not supported. Use ListFilesAsync instead."

      member this.ListFilesAsync(locationUri, [<Optional>] ?cancellationToken) = task {
        let ct = defaultArg cancellationToken CancellationToken.None

        logger.LogDebug("Listing files in {uri}", locationUri)

        match parseVirtualProjectUri locationUri with
        | Some(MigrationList projectId) ->
          let! migrations = vpr.GetMigrations projectId ct

          let basePath = locationUri.ToString().TrimEnd('/') + "/"

          return
            migrations
            |> List.map(fun m -> Uri($"{basePath}{m.timestamp}_{m.name}.sql"))
            :> Uri seq

        | Some(ProjectConfig _) ->
          return failwith "Cannot list files from config URI"

        | Some(Migration _) ->
          return failwith "Cannot list files from single migration URI"

        | None -> return failwith $"Unsupported URI: {locationUri}"
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
        | None -> return failwith $"Project with id {project} not found"
        | Some(project: VirtualProject, migrations: VirtualMigration list) ->
          let config = {
            project.ToMigrondiConfig() with
                migrations = "./migrations"
          }

          let projectRoot = Directory.CreateDirectory(path)
          let migrationsDir = projectRoot.CreateSubdirectory("migrations")

          let configPath = Path.Combine(projectRoot.FullName, "migrondi.json")

          let configContent = MiSerializer.Encode config

          do! File.WriteAllTextAsync(configPath, configContent, token)

          do!
            migrations
            |> List.map(fun (vm: VirtualMigration) -> asyncEx {
              let! token = Async.CancellationToken
              let content: string = MiSerializer.Encode(vm.ToMigration())

              let migrationPath =
                Path.Combine(
                  migrationsDir.FullName,
                  $"{vm.timestamp}_{vm.name}.sql"
                )

              do! File.WriteAllTextAsync(migrationPath, content, token)
            })
            |> Async.Parallel
            |> Async.Ignore

          return projectRoot.FullName
      }

      member _.ImportFromLocal(project: LocalProject) : CancellableTask<Guid> =
        importProjectFromPath project.migrondiConfigPath project.name

      member _.ImportFromLocal(projectConfigPath: string) =
        let projectName = Path.GetDirectoryName projectConfigPath |> nonNull
        importProjectFromPath projectConfigPath projectName
  }
