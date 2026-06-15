namespace MigrondiUI.Mcp

open System

open IcedTasks

open MigrondiUI
open Migrondi.Core

module McpTools =

  let listProjects(env: McpEnvironment) = cancellableTask {
    let! projects = env.projects.List()
    return projects
  }

  let getProject (env: McpEnvironment) (projectId: Guid) = cancellableTask {
    let! project = env.projects.Get projectId
    return project
  }

  let listMigrations (env: McpEnvironment) (projectId: Guid) = cancellableTask {
    match! env.projects.Get projectId with
    | None -> return List.empty
    | Some project ->
      let ops = env.migrondiFactory.Create project
      let! ct = CancellableTask.getCancellationToken()
      let! migrations = ops.Core.MigrationsListAsync ct
      return migrations |> Seq.toList
  }

  type GetMigrationError =
    | ProjectNotFound
    | LocalProjectsNotSupported
    | MigrationNotFound

  let getMigration
    (env: McpEnvironment)
    (projectId: Guid)
    (migrationName: string)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None -> return Error GetMigrationError.ProjectNotFound
      | Some project ->
        match project with
        | Local _ -> return Error GetMigrationError.LocalProjectsNotSupported
        | Virtual _ ->
          let ops = env.migrondiFactory.Create project
          let! migration = ops.GetMigration migrationName

          match migration with
          | None -> return Error GetMigrationError.MigrationNotFound
          | Some m ->
            return
              Ok {
                id = Guid.NewGuid()
                name = m.name
                timestamp = m.timestamp
                upContent = m.upContent
                downContent = m.downContent
                projectId = projectId
                manualTransaction = m.manualTransaction
              }
    }

  let dryRunMigrations
    (env: McpEnvironment)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None -> return List.empty
      | Some project ->
        let ops = env.migrondiFactory.Create project
        let! ct = CancellableTask.getCancellationToken()

        let! migrations = ops.Core.DryRunUpAsync(?amount = amount, cancellationToken = ct)

        return migrations |> Seq.toList
    }

  let dryRunRollback
    (env: McpEnvironment)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None -> return List.empty
      | Some project ->
        let ops = env.migrondiFactory.Create project
        let! ct = CancellableTask.getCancellationToken()

        let! migrations = ops.Core.DryRunDownAsync(?amount = amount, cancellationToken = ct)

        return migrations |> Seq.toList
    }

  type RunMigrationsError =
    | ProjectNotFound
    | ExecutionFailed of string

  let runMigrations
    (env: McpEnvironment)
    (projectId: Guid)
    (amount: int option)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None -> return Error RunMigrationsError.ProjectNotFound
      | Some project ->
        let ops = env.migrondiFactory.Create project

        try
          let! ct = CancellableTask.getCancellationToken()

          let! migrations = ops.Core.RunUpAsync(?amount = amount, cancellationToken = ct)

          return Ok(migrations |> Seq.toList)
        with ex ->
          return Error(RunMigrationsError.ExecutionFailed ex.Message)
    }

  type RunRollbackError =
    | ProjectNotFound
    | ExecutionFailed of string

  let runRollback (env: McpEnvironment) (projectId: Guid) (amount: int option) = cancellableTask {
    match! env.projects.Get projectId with
    | None -> return Error RunRollbackError.ProjectNotFound
    | Some project ->
      let ops = env.migrondiFactory.Create project

      try
        let! ct = CancellableTask.getCancellationToken()

        let! migrations = ops.Core.RunDownAsync(?amount = amount, cancellationToken = ct)

        return Ok(migrations |> Seq.toList)
      with ex ->
        return Error(RunRollbackError.ExecutionFailed ex.Message)
  }

  type CreateMigrationError =
    | ProjectNotFound
    | InvalidMigrationName of string
    | CreationFailed of string

  let createMigration
    (env: McpEnvironment)
    (projectId: Guid)
    (name: string)
    (upContent: string option)
    (downContent: string option)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None -> return Error CreateMigrationError.ProjectNotFound
      | Some project ->
        match MigrationName.Validate name with
        | Error errorMsg ->
          return Error(CreateMigrationError.InvalidMigrationName errorMsg)
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
              Ok {|
                name = migration.name
                timestamp = migration.timestamp
                fullName = $"{migration.timestamp}_{migration.name}"
              |}
          with ex ->
            return Error(CreateMigrationError.CreationFailed ex.Message)
    }

  type UpdateMigrationError =
    | ProjectNotFound
    | MigrationNotFound
    | AlreadyApplied
    | DatabaseError of string

  let updateMigration
    (env: McpEnvironment)
    (guid: Guid)
    (name: string)
    (upContent: string)
    (downContent: string)
    =
    cancellableTask {
      match! env.projects.Get guid with
      | None -> return Error UpdateMigrationError.ProjectNotFound
      | Some project ->
        let ops = env.migrondiFactory.Create project
        let! existing = ops.GetMigration name

        match existing with
        | None -> return Error UpdateMigrationError.MigrationNotFound
        | Some m ->
          let updatedMigration = {
            m with
                upContent = upContent
                downContent = downContent
          }

          let! result = ops.UpdateMigration updatedMigration

          match result with
          | Ok _ -> return Ok()
          | Error(Services.MigrationCrudError.AlreadyApplied _) ->
            return Error UpdateMigrationError.AlreadyApplied
          | Error(Services.MigrationCrudError.NotFound _) ->
            return Error UpdateMigrationError.MigrationNotFound
          | Error(Services.MigrationCrudError.DatabaseError msg) ->
            return Error(UpdateMigrationError.DatabaseError msg)
    }

  type DeleteMigrationError =
    | ProjectNotFound
    | MigrationNotFound
    | AlreadyApplied
    | DatabaseError of string

  let deleteMigration (env: McpEnvironment) (guid: Guid) (name: string) = cancellableTask {
    match! env.projects.Get guid with
    | None -> return Error DeleteMigrationError.ProjectNotFound
    | Some project ->
      let ops = env.migrondiFactory.Create project
      let! result = ops.DeleteMigration name

      match result with
      | Ok _ -> return Ok()
      | Error(Services.MigrationCrudError.AlreadyApplied _) ->
        return Error DeleteMigrationError.AlreadyApplied
      | Error(Services.MigrationCrudError.NotFound _) ->
        return Error DeleteMigrationError.MigrationNotFound
      | Error(Services.MigrationCrudError.DatabaseError msg) ->
        return Error(DeleteMigrationError.DatabaseError msg)
  }

  type CreateProjectError = CreationFailed of string

  let createVirtualProject
    (env: McpEnvironment)
    (name: string)
    (connection: string)
    (driver: MigrondiDriver)
    (description: string option)
    (tableName: string option)
    =
    cancellableTask {
      let args: Database.InsertVirtualProjectArgs = {
        name = name
        description = description
        connection = connection
        tableName = defaultArg tableName "migrations"
        driver = driver.AsString
      }

      try
        let! projectId = env.projects.CreateVirtual args
        return Ok projectId
      with ex ->
        return Error(CreateProjectError.CreationFailed ex.Message)
    }

  type UpdateProjectError =
    | ProjectNotFound
    | LocalProjectsNotSupported
    | UpdateFailed of string

  let updateVirtualProject
    (env: McpEnvironment)
    (projectId: Guid)
    (name: string option)
    (connection: string option)
    (tableName: string option)
    (driver: MigrondiDriver option)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None -> return Error UpdateProjectError.ProjectNotFound
      | Some(Virtual p) ->
        let updatedProject: VirtualProject = {
          p with
              name = defaultArg name p.name
              connection = defaultArg connection p.connection
              tableName = defaultArg tableName p.tableName
              driver = defaultArg driver p.driver
        }

        try
          do! env.projects.UpdateVirtual updatedProject
          return Ok()
        with ex ->
          return Error(UpdateProjectError.UpdateFailed ex.Message)
      | Some(Local _) ->
        return Error UpdateProjectError.LocalProjectsNotSupported
    }

  type DeleteProjectError =
    | ProjectNotFound
    | HasAppliedMigrations

  let deleteProject (env: McpEnvironment) (projectId: Guid) = cancellableTask {
    match! env.projects.DeleteProject(projectId, Services.DeleteKind.Soft) with
    | Ok _ -> return Ok()
    | Error Services.ProjectDeleteError.NotFound ->
      return Error DeleteProjectError.ProjectNotFound
    | Error Services.ProjectDeleteError.HasAppliedMigrations ->
      return Error DeleteProjectError.HasAppliedMigrations
  }

  type ExportProjectError =
    | ProjectNotFound
    | LocalProjectsNotSupported
    | ExportFailed of string

  let exportVirtualProject
    (env: McpEnvironment)
    (projectId: Guid)
    (exportPath: string)
    =
    cancellableTask {
      match! env.projects.Get projectId with
      | None -> return Error ExportProjectError.ProjectNotFound
      | Some(Virtual _) ->
        try
          let! exportedPath = env.projects.Export(projectId, exportPath)
          return Ok exportedPath
        with ex ->
          return Error(ExportProjectError.ExportFailed ex.Message)
      | Some(Local _) ->
        return Error ExportProjectError.LocalProjectsNotSupported
    }

  type ImportProjectError = ImportFailed of string

  let importFromLocal (env: McpEnvironment) (configPath: string) = cancellableTask {
    try
      let! projectId = env.projects.Import configPath
      return Ok projectId
    with ex ->
      return Error(ImportProjectError.ImportFailed ex.Message)
  }
