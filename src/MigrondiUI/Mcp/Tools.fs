namespace MigrondiUI.Mcp

open System

open IcedTasks

open MigrondiUI.Projects
open Migrondi.Core

open McpResults
open McpRuntime
open McpResultMapper

module McpTools =

  let listProjects (env: McpEnvironment) = cancellableTask {
    let! localProjects = env.lProjects.GetProjects()
    let! vProjects = env.vProjects.GetProjects()

    let result: McpResults.ListProjectsResult = {
      local =
        localProjects
        |> List.map McpResults.LocalProjectSummary.FromLocalProject
      virtualProjects =
        vProjects
        |> List.map McpResults.VirtualProjectSummary.FromVirtualProject
    }

    return
      McpResultMapper.fromEncoder McpResults.ListProjectsResult.Encoder result
  }

  let getProject (env: McpEnvironment) (projectId: string) = cancellableTask {
    match Guid.TryParse projectId with
    | false, _ ->
      return
        McpResultMapper.fromEncoder
          McpResults.GetProjectResult.Encoder
          (McpResults.GetProjectResult.ProjectNotFound
            "projectId must be a valid GUID")
    | true, id ->
      let! localProject = env.lProjects.GetProjectById id

      match localProject with
      | Some p ->
        return
          McpResultMapper.fromEncoder
            McpResults.GetProjectResult.Encoder
            (McpResults.GetProjectResult.LocalProject(
              McpResults.LocalProjectDetail.FromLocalProject p
            ))
      | None ->
        let! vProject = env.vProjects.GetProjectById id

        match vProject with
        | Some p ->
          return
            McpResultMapper.fromEncoder
              McpResults.GetProjectResult.Encoder
              (McpResults.GetProjectResult.VirtualProject(
                McpResults.VirtualProjectDetail.FromVirtualProject p
              ))
        | None ->
          return
            McpResultMapper.fromEncoder
              McpResults.GetProjectResult.Encoder
              (McpResults.GetProjectResult.ProjectNotFound
                $"Project {projectId} not found")
  }

  let listMigrations (env: McpEnvironment) (projectId: string) = cancellableTask {
    let! result = cancellableTask {
      match Guid.TryParse projectId with
      | false, _ -> return McpResults.ListMigrationsResult.Empty
      | true, id ->
        match! McpRuntime.findProject env.lProjects env.vProjects id with
        | None -> return McpResults.ListMigrationsResult.Empty
        | Some project ->
          match McpRuntime.getMigrondi env project with
          | None -> return McpResults.ListMigrationsResult.Empty
          | Some migrondi ->
            let! ct = CancellableTask.getCancellationToken()
            let! migrations = migrondi.MigrationsListAsync ct
            return McpResults.ListMigrationsResult.FromMigrations migrations
    }

    return
      McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder result
  }

  let getMigration (env: McpEnvironment) (guid: Guid) (migrationName: string) = cancellableTask {
    match! env.vProjects.GetMigrationByName guid migrationName with
    | None ->
      return
        McpResultMapper.fromEncoder
          McpResults.GetMigrationResult.Encoder
          (McpResults.GetMigrationResult.MigrationNotFound
            $"Migration '{migrationName}' not found")
    | Some m ->
      return
        McpResultMapper.fromEncoder
          McpResults.GetMigrationResult.Encoder
          (McpResults.GetMigrationResult.MigrationFound(
            McpResults.MigrationDetail.FromVirtualMigration m
          ))
  }

  let dryRunMigrations
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match Guid.TryParse projectId with
        | false, _ -> return McpResults.DryRunResult.Empty
        | true, id ->
          match! McpRuntime.findProject env.lProjects env.vProjects id with
          | None -> return McpResults.DryRunResult.Empty
          | Some project ->
            match McpRuntime.getMigrondi env project with
            | None -> return McpResults.DryRunResult.Empty
            | Some migrondi ->
              let! ct = CancellableTask.getCancellationToken()

              let! migrations =
                match amount with
                | Some a ->
                  migrondi.DryRunUpAsync(amount = a, cancellationToken = ct)
                | None -> migrondi.DryRunUpAsync(cancellationToken = ct)

              return McpResults.DryRunResult.FromMigrations migrations
      }

      return McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder result
    }

  let dryRunRollback
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match Guid.TryParse projectId with
        | false, _ -> return McpResults.DryRunResult.Empty
        | true, id ->
          match! McpRuntime.findProject env.lProjects env.vProjects id with
          | None -> return McpResults.DryRunResult.Empty
          | Some project ->
            match McpRuntime.getMigrondi env project with
            | None -> return McpResults.DryRunResult.Empty
            | Some migrondi ->
              let! ct = CancellableTask.getCancellationToken()

              let! migrations =
                match amount with
                | Some a ->
                  migrondi.DryRunDownAsync(amount = a, cancellationToken = ct)
                | None -> migrondi.DryRunDownAsync(cancellationToken = ct)

              return McpResults.DryRunResult.FromMigrations migrations
      }

      return McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder result
    }

module McpWriteTools =
  open MigrondiUI

  let runMigrations
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.MigrationsResult.Error "projectId must be a valid GUID"
        | true, id ->
          match! McpRuntime.findProject env.lProjects env.vProjects id with
          | None ->
            return
              McpResults.MigrationsResult.Error $"Project {projectId} not found"
          | Some project ->
            match McpRuntime.getMigrondi env project with
            | None ->
              return
                McpResults.MigrationsResult.Error
                  "Project has no valid configuration"
            | Some migrondi ->
              try
                let! ct = CancellableTask.getCancellationToken()

                let! migrations =

                  match amount with
                  | Some a ->
                    migrondi.RunUpAsync(amount = a, cancellationToken = ct)
                  | None -> migrondi.RunUpAsync(cancellationToken = ct)

                return
                  McpResults.MigrationsResult.FromMigrationRecords migrations
              with ex ->
                return
                  McpResults.MigrationsResult.Error
                    $"Failed to apply migrations: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder result
    }

  let runRollback
    (env: McpEnvironment)
    (projectId: string)
    (amount: int option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.MigrationsResult.Error "projectId must be a valid GUID"
        | true, id ->
          match! McpRuntime.findProject env.lProjects env.vProjects id with
          | None ->
            return
              McpResults.MigrationsResult.Error $"Project {projectId} not found"
          | Some project ->
            match McpRuntime.getMigrondi env project with
            | None ->
              return
                McpResults.MigrationsResult.Error
                  "Project has no valid configuration"
            | Some migrondi ->
              try
                let! ct = CancellableTask.getCancellationToken()

                let! migrations =
                  match amount with
                  | Some a ->
                    migrondi.RunDownAsync(amount = a, cancellationToken = ct)
                  | None -> migrondi.RunDownAsync(cancellationToken = ct)

                return
                  McpResults.MigrationsResult.FromMigrationRecords migrations
              with ex ->
                return
                  McpResults.MigrationsResult.Error
                    $"Failed to rollback migrations: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder result
    }

  let createMigration
    (env: McpEnvironment)
    (projectId: string)
    (name: string)
    (upContent: string option)
    (downContent: string option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.CreateMigrationResult.CreateMigrationError
              "projectId must be a valid GUID"
        | true, projectId ->
          match! env.vProjects.GetProjectById projectId with
          | None ->
            return
              McpResults.CreateMigrationResult.CreateMigrationError
                $"Virtual project {projectId} not found"
          | Some _ ->
            match MigrationName.Validate name with
            | Error errorMsg ->
              return
                McpResults.CreateMigrationResult.CreateMigrationError errorMsg
            | Ok _ ->
              let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()

              let migration: VirtualMigration = {
                id = Guid.NewGuid()
                name = name
                timestamp = timestamp
                upContent = defaultArg upContent ""
                downContent = defaultArg downContent ""
                projectId = projectId
                manualTransaction = false
              }

              try
                let! id = env.vProjects.InsertMigration migration

                return
                  McpResults.CreateMigrationResult.MigrationCreated {|
                    id = id
                    name = migration.name
                    timestamp = migration.timestamp
                    fullName = $"{migration.timestamp}_{migration.name}"
                  |}
              with ex ->
                return
                  McpResults.CreateMigrationResult.CreateMigrationError
                    $"Failed to create migration: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder
          McpResults.CreateMigrationResult.Encoder
          result
    }

  let updateMigration
    (env: McpEnvironment)
    (guid: Guid)
    (name: string)
    (upContent: string)
    (downContent: string)
    =
    cancellableTask {
      let! result = cancellableTask {
        match! env.vProjects.GetMigrationByName guid name with
        | None ->
          return McpResults.SuccessResult.Error $"Migration '{name}' not found"
        | Some m ->
          let updatedMigration: VirtualMigration = {
            m with
                upContent = upContent
                downContent = downContent
          }

          try
            do! env.vProjects.UpdateMigration updatedMigration

            return
              McpResults.SuccessResult.Ok
                $"Migration '{name}' updated successfully"
          with ex ->
            return
              McpResults.SuccessResult.Error
                $"Failed to update migration: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  let deleteMigration (env: McpEnvironment) (guid: Guid) (name: string) = cancellableTask {
    let! result = cancellableTask {
      match! env.vProjects.GetMigrationByName guid name with
      | None ->
        return McpResults.SuccessResult.Error $"Migration '{name}' not found"
      | Some _ ->
        try
          do! env.vProjects.RemoveMigrationByName guid name

          return
            McpResults.SuccessResult.Ok
              $"Migration '{name}' deleted successfully"
        with ex ->
          return
            McpResults.SuccessResult.Error
              $"Failed to delete migration: {ex.Message}"
    }

    return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
  }

  let createVirtualProject
    (env: McpEnvironment)
    (name: string)
    (connection: string)
    (driver: string)
    (description: string option)
    (tableName: string option)
    =
    cancellableTask {
      let! result = cancellableTask {
        let driverValue =
          try
            MigrondiDriver.FromString driver |> Some
          with _ ->
            None

        match driverValue with
        | None ->
          return
            McpResults.CreateProjectResult.CreateProjectError
              $"Invalid driver '{driver}'. Valid options: sqlite, postgres, mysql, mssql"
        | Some driver ->
          let newProject: NewVirtualProjectArgs = {
            name = name
            description = defaultArg description ""
            connection = connection
            tableName = defaultArg tableName "migrations"
            driver = driver
          }

          try
            let! projectId = env.vProjects.InsertProject newProject

            return
              McpResults.CreateProjectResult.ProjectCreated {|
                id = projectId
                name = name
                driver = driver.AsString
                tableName = newProject.tableName
              |}
          with ex ->
            return
              McpResults.CreateProjectResult.CreateProjectError
                $"Failed to create project: {ex.Message}"
      }

      return
        McpResultMapper.fromEncoder
          McpResults.CreateProjectResult.Encoder
          result
    }

  let updateVirtualProject
    (env: McpEnvironment)
    (projectId: string)
    (name: string option)
    (connection: string option)
    (tableName: string option)
    (driver: string option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match Guid.TryParse projectId with
        | false, _ ->
          return McpResults.SuccessResult.Error "projectId must be a valid GUID"
        | true, id ->
          match! env.vProjects.GetProjectById id with
          | None ->
            return
              McpResults.SuccessResult.Error
                $"Virtual project {projectId} not found"
          | Some p ->
            let driverValue =
              driver
              |> Option.bind(fun d ->
                try
                  MigrondiDriver.FromString d |> Some
                with _ ->
                  None
              )
              |> Option.defaultValue p.driver

            let updatedProject: VirtualProject = {
              p with
                  name = defaultArg name p.name
                  connection = defaultArg connection p.connection
                  tableName = defaultArg tableName p.tableName
                  driver = driverValue
            }

            try
              do! env.vProjects.UpdateProject updatedProject
              MigrondiCache.invalidate env.migrondiCache p.id

              return
                McpResults.SuccessResult.Ok
                  $"Project '{updatedProject.name}' updated successfully"
            with ex ->
              return
                McpResults.SuccessResult.Error
                  $"Failed to update project: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder result
    }

  let deleteProject (env: McpEnvironment) (projectId: string) = cancellableTask {
    let! result = cancellableTask {
      match Guid.TryParse projectId with
      | false, _ ->
        return McpResults.ErrorResult.Create "projectId must be a valid GUID"
      | true, id ->
        match! env.lProjects.GetProjectById id with
        | Some _ ->
          return
            McpResults.ErrorResult.Create
              "Deleting local projects is not supported via MCP. Remove the project manually or use the GUI."
        | None ->
          match! env.vProjects.GetProjectById id with
          | None ->
            return
              McpResults.ErrorResult.Create $"Project {projectId} not found"
          | Some _ ->
            return
              McpResults.ErrorResult.Create
                "Deleting virtual projects is not yet implemented"
    }

    return McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder result
  }

  let exportVirtualProject
    (env: McpEnvironment)
    (projectId: string)
    (exportPath: string)
    =
    cancellableTask {
      let! result = cancellableTask {
        match Guid.TryParse projectId with
        | false, _ ->
          return
            McpResults.ExportResult.ExportError "projectId must be a valid GUID"
        | true, id ->
          match! env.vProjects.GetProjectById id with
          | None ->
            return
              McpResults.ExportResult.ExportError
                $"Virtual project {projectId} not found"
          | Some p ->
            try
              let! exportedPath = env.vfs.ExportToLocal(p.id, exportPath)

              return
                McpResults.ExportResult.ExportSuccess {| path = exportedPath |}
            with ex ->
              return
                McpResults.ExportResult.ExportError
                  $"Failed to export project: {ex.Message}"
      }

      return McpResultMapper.fromEncoder McpResults.ExportResult.Encoder result
    }

  let importFromLocal (env: McpEnvironment) (configPath: string) = cancellableTask {
    let! result = cancellableTask {
      try
        let! projectId = env.vfs.ImportFromLocal configPath

        return McpResults.ImportResult.ImportSuccess {| projectId = projectId |}
      with ex ->
        return
          McpResults.ImportResult.ImportError
            $"Failed to import project: {ex.Message}"
    }

    return McpResultMapper.fromEncoder McpResults.ImportResult.Encoder result
  }