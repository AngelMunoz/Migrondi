namespace MigrondiUI.Mcp

open System

open IcedTasks

open MigrondiUI
open Migrondi.Core

module McpTools =

  let listProjects(env: McpEnvironment) = cancellableTask {
    let! projects = env.projects.List()

    let result: McpResults.ListProjectsResult = {
      local =
        projects
        |> List.choose (function
          | Local p -> Some(McpResults.LocalProjectSummary.FromLocalProject p)
          | Virtual _ -> None)
      virtualProjects =
        projects
        |> List.choose (function
          | Virtual p ->
            Some(McpResults.VirtualProjectSummary.FromVirtualProject p)
          | Local _ -> None)
    }

    return
      result
      |> McpResultMapper.fromEncoder McpResults.ListProjectsResult.Encoder
  }

  let getProject (env: McpEnvironment) (projectId: Guid) = cancellableTask {
    let! project = env.projects.Get projectId

    match project with
    | Some(Local p) ->
      return
        McpResults.LocalProjectDetail.FromLocalProject p
        |> McpResults.GetProjectResult.LocalProject
        |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder

    | Some(Virtual p) ->
      return
        McpResults.VirtualProjectDetail.FromVirtualProject p
        |> McpResults.GetProjectResult.VirtualProject
        |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder
    | None ->
      return
        McpResults.GetProjectResult.ProjectNotFound
          $"Project {projectId} not found"
        |> McpResultMapper.fromEncoder McpResults.GetProjectResult.Encoder
  }

  let listMigrations (env: McpEnvironment) (projectId: Guid) = cancellableTask {
    let! result = cancellableTask {
      match! env.projects.Get projectId with
      | None -> return McpResults.ListMigrationsResult.Empty
      | Some project ->
        let ops = env.migrondiFactory.Create project
        let! ct = CancellableTask.getCancellationToken()
        let! migrations = ops.Core.MigrationsListAsync ct
        return McpResults.ListMigrationsResult.FromMigrations migrations
    }

    return
      result
      |> McpResultMapper.fromEncoder McpResults.ListMigrationsResult.Encoder
  }

  let getMigration
    (env: McpEnvironment)
    (projectId: Guid)
    (migrationName: string)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None ->
        return
          McpResults.GetMigrationResult.MigrationNotFound
            $"Project {projectId} not found"
          |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
      | Some project ->
        match project with
        | Local _ ->
          return
            McpResults.GetMigrationResult.MigrationNotFound
              "Get migration is only supported for virtual projects"
            |> McpResultMapper.fromEncoder McpResults.GetMigrationResult.Encoder
        | Virtual _ ->
          let ops = env.migrondiFactory.Create project
          let! migration = ops.GetMigration migrationName

          match migration with
          | None ->
            return
              McpResults.GetMigrationResult.MigrationNotFound
                $"Migration '{migrationName}' not found"
              |> McpResultMapper.fromEncoder
                McpResults.GetMigrationResult.Encoder
          | Some m ->
            return
              {
                id = Guid.NewGuid()
                name = m.name
                timestamp = m.timestamp
                upContent = m.upContent
                downContent = m.downContent
                projectId = projectId
                manualTransaction = m.manualTransaction
              }
              |> McpResults.MigrationDetail.FromVirtualMigration
              |> McpResults.GetMigrationResult.MigrationFound
              |> McpResultMapper.fromEncoder
                McpResults.GetMigrationResult.Encoder
    }

  let dryRunMigrations
    (env: McpEnvironment)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match! env.projects.Get projectId with
        | None -> return McpResults.DryRunResult.Empty
        | Some project ->
          let ops = env.migrondiFactory.Create project
          let! ct = CancellableTask.getCancellationToken()

          let! migrations =
            match amount with
            | Some a -> ops.Core.DryRunUpAsync(a, cancellationToken = ct)
            | None -> ops.Core.DryRunUpAsync(cancellationToken = ct)

          return McpResults.DryRunResult.FromMigrations migrations
      }

      return
        result |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
    }

  let dryRunRollback
    (env: McpEnvironment)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      let! result = cancellableTask {
        match! env.projects.Get projectId with
        | None -> return McpResults.DryRunResult.Empty
        | Some project ->
          let ops = env.migrondiFactory.Create project
          let! ct = CancellableTask.getCancellationToken()

          let! migrations =
            match amount with
            | Some a -> ops.Core.DryRunDownAsync(a, cancellationToken = ct)
            | None -> ops.Core.DryRunDownAsync(cancellationToken = ct)

          return McpResults.DryRunResult.FromMigrations migrations
      }

      return
        result |> McpResultMapper.fromEncoder McpResults.DryRunResult.Encoder
    }

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
          match! env.projects.Get id with
          | None ->
            return
              McpResults.MigrationsResult.Error $"Project {projectId} not found"
          | Some project ->
            let ops = env.migrondiFactory.Create project

            try
              let! ct = CancellableTask.getCancellationToken()

              let! migrations =

                match amount with
                | Some a ->
                  ops.Core.RunUpAsync(amount = a, cancellationToken = ct)
                | None -> ops.Core.RunUpAsync(cancellationToken = ct)

              return McpResults.MigrationsResult.FromMigrationRecords migrations
            with ex ->
              return
                McpResults.MigrationsResult.Error
                  $"Failed to apply migrations: {ex.Message}"
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
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
          match! env.projects.Get id with
          | None ->
            return
              McpResults.MigrationsResult.Error $"Project {projectId} not found"
          | Some project ->
            let ops = env.migrondiFactory.Create project

            try
              let! ct = CancellableTask.getCancellationToken()

              let! migrations =
                match amount with
                | Some a ->
                  ops.Core.RunDownAsync(amount = a, cancellationToken = ct)
                | None -> ops.Core.RunDownAsync(cancellationToken = ct)

              return McpResults.MigrationsResult.FromMigrationRecords migrations
            with ex ->
              return
                McpResults.MigrationsResult.Error
                  $"Failed to rollback migrations: {ex.Message}"
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.MigrationsResult.Encoder
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
        | true, id ->
          match! env.projects.Get id with
          | None ->
            return
              McpResults.CreateMigrationResult.CreateMigrationError
                $"Project {projectId} not found"
          | Some project ->
            match MigrationName.Validate name with
            | Error errorMsg ->
              return
                McpResults.CreateMigrationResult.CreateMigrationError errorMsg
            | Ok _ ->
              let ops = env.migrondiFactory.Create project
              let! ct = CancellableTask.getCancellationToken()

              try
                let! migration =
                  ops.Core.RunNewAsync(
                    name,
                    ?upContent = upContent,
                    ?downContent = downContent,
                    cancellationToken = ct
                  )

                return
                  McpResults.CreateMigrationResult.MigrationCreated {|
                    id = Guid.NewGuid()
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
        result
        |> McpResultMapper.fromEncoder McpResults.CreateMigrationResult.Encoder
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
        match! env.projects.Get guid with
        | None ->
          return McpResults.SuccessResult.Error $"Project {guid} not found"
        | Some project ->
          let ops = env.migrondiFactory.Create project
          let! existing = ops.GetMigration name

          match existing with
          | None ->
            return
              McpResults.SuccessResult.Error $"Migration '{name}' not found"
          | Some m ->
            let updatedMigration = {
              m with
                  upContent = upContent
                  downContent = downContent
            }

            let! result = ops.UpdateMigration updatedMigration

            match result with
            | Ok _ ->
              return
                McpResults.SuccessResult.Ok
                  $"Migration '{name}' updated successfully"
            | Error(Services.MigrationCrudError.AlreadyApplied n) ->
              return
                McpResults.SuccessResult.Error
                  $"Migration '{n}' has already been applied"
            | Error(Services.MigrationCrudError.NotFound n) ->
              return McpResults.SuccessResult.Error $"Migration '{n}' not found"
            | Error(Services.MigrationCrudError.DatabaseError msg) ->
              return McpResults.SuccessResult.Error msg
      }

      return
        result |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
    }

  let deleteMigration (env: McpEnvironment) (guid: Guid) (name: string) = cancellableTask {
    let! result = cancellableTask {
      match! env.projects.Get guid with
      | None ->
        return McpResults.SuccessResult.Error $"Project {guid} not found"
      | Some project ->
        let ops = env.migrondiFactory.Create project
        let! result = ops.DeleteMigration name

        match result with
        | Ok _ ->
          return
            McpResults.SuccessResult.Ok
              $"Migration '{name}' deleted successfully"
        | Error(Services.MigrationCrudError.AlreadyApplied n) ->
          return
            McpResults.SuccessResult.Error
              $"Migration '{n}' has already been applied"
        | Error(Services.MigrationCrudError.NotFound n) ->
          return McpResults.SuccessResult.Error $"Migration '{n}' not found"
        | Error(Services.MigrationCrudError.DatabaseError msg) ->
          return McpResults.SuccessResult.Error msg
    }

    return
      result |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
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
          let args: Database.InsertVirtualProjectArgs = {
            name = name
            description = description
            connection = connection
            tableName = defaultArg tableName "migrations"
            driver = driver.AsString
          }

          try
            let! projectId = env.projects.CreateVirtual args

            return
              McpResults.CreateProjectResult.ProjectCreated {|
                id = projectId
                name = name
                driver = driver.AsString
                tableName = args.tableName
              |}
          with ex ->
            return
              McpResults.CreateProjectResult.CreateProjectError
                $"Failed to create project: {ex.Message}"
      }

      return
        result
        |> McpResultMapper.fromEncoder McpResults.CreateProjectResult.Encoder
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
          match! env.projects.Get id with
          | None ->
            return
              McpResults.SuccessResult.Error $"Project {projectId} not found"
          | Some(Virtual p) ->
            let driverValue =
              driver
              |> Option.bind(fun d ->
                try
                  MigrondiDriver.FromString d |> Some
                with _ ->
                  None)
              |> Option.defaultValue p.driver

            let updatedProject: VirtualProject = {
              p with
                  name = defaultArg name p.name
                  connection = defaultArg connection p.connection
                  tableName = defaultArg tableName p.tableName
                  driver = driverValue
            }

            try
              do! env.projects.UpdateVirtual updatedProject

              return
                McpResults.SuccessResult.Ok
                  $"Project '{updatedProject.name}' updated successfully"
            with ex ->
              return
                McpResults.SuccessResult.Error
                  $"Failed to update project: {ex.Message}"
          | Some(Local _) ->
            return
              McpResults.SuccessResult.Error
                "Cannot update local projects via MCP"
      }

      return
        result |> McpResultMapper.fromEncoder McpResults.SuccessResult.Encoder
    }

  let deleteProject (env: McpEnvironment) (projectId: string) = cancellableTask {
    let! result = cancellableTask {
      match Guid.TryParse projectId with
      | false, _ ->
        return McpResults.ErrorResult.Create "projectId must be a valid GUID"
      | true, id ->
        match! env.projects.DeleteProject(id, Services.DeleteKind.Soft) with
        | Ok _ ->
          return
            McpResults.SuccessResult.Ok $"Project {projectId} deleted"
            |> fun r -> box r |> unbox
        | Error Services.ProjectDeleteError.NotFound ->
          return McpResults.ErrorResult.Create $"Project {projectId} not found"
        | Error Services.ProjectDeleteError.HasAppliedMigrations ->
          return
            McpResults.ErrorResult.Create
              "Cannot delete project with applied migrations"
    }

    return result |> McpResultMapper.fromEncoder McpResults.ErrorResult.Encoder
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
          match! env.projects.Get id with
          | None ->
            return
              McpResults.ExportResult.ExportError
                $"Project {projectId} not found"
          | Some(Virtual _) ->
            try
              let! exportedPath = env.projects.Export(id, exportPath)

              return
                McpResults.ExportResult.ExportSuccess {| path = exportedPath |}
            with ex ->
              return
                McpResults.ExportResult.ExportError
                  $"Failed to export project: {ex.Message}"
          | Some(Local _) ->
            return
              McpResults.ExportResult.ExportError "Cannot export local projects"
      }

      return
        result |> McpResultMapper.fromEncoder McpResults.ExportResult.Encoder
    }

  let importFromLocal (env: McpEnvironment) (configPath: string) = cancellableTask {
    let! result = cancellableTask {
      try
        let! projectId = env.projects.Import configPath

        return McpResults.ImportResult.ImportSuccess {| projectId = projectId |}
      with ex ->
        return
          McpResults.ImportResult.ImportError
            $"Failed to import project: {ex.Message}"
    }

    return result |> McpResultMapper.fromEncoder McpResults.ImportResult.Encoder
  }
