namespace MigrondiUI

open System
open System.Data
open Donald
open IcedTasks

module Queries =

  [<Literal>]
  let GetLocalProjects =
    """
    select
      p.id as id, p.name as name, p.description as description, lp.config_path as config_path
    from projects as p
    left join local_projects as lp
      on lp.project_id = p.id
    where lp.id is not null;
    """

  [<Literal>]
  let GetLocalProjectById =
    """
    select
      p.id as id, p.name as name, p.description as description, lp.config_path as config_path
    from projects as p
    left join local_projects as lp
      on lp.project_id = p.id
    where p.id = @id and lp.id is not null;
    """

  [<Literal>]
  let InsertProject =
    """
    insert into projects (id, name, description)
    values (@id, @name, @description)
    """

  [<Literal>]
  let InsertLocalProject =
    """
    insert into local_projects (id, config_path, project_id)
    values (@id, @config_path, @project_id);
    """

  [<Literal>]
  let InsertVirtualProject =
    """
    insert into virtual_projects (id, connection, table_name, driver, project_id)
    values (@id, @connection, @table_name, @driver, @project_id);
    """

  [<Literal>]
  let UpdateProjectQuery =
    """
    update projects
    set
      name = coalesce(@name, name),
      description = coalesce(@description, description)
    where id = @id;
    """

  [<Literal>]
  let UpdateLocalProjectConfigPathQuery =
    """
    update local_projects
    set
      config_path = coalesce(@config_path, config_path)
    where project_id = @project_id;
    """

  [<Literal>]
  let GetVirtualProjects =
    """
    select
      vp.id as id, p.name as name, p.description as description,
      vp.connection as connection, vp.table_name as table_name, vp.driver as driver,
      p.id as project_id
    from projects as p
    left join virtual_projects as vp
      on vp.project_id = p.id
    where vp.id is not null;
    """

  [<Literal>]
  let GetVirtualProjectById =
    """
    select
      vp.id as id, p.name as name, p.description as description,
      vp.connection as connection, vp.table_name as table_name, vp.driver as driver,
      p.id as project_id
    from projects as p
    left join virtual_projects as vp
      on vp.project_id = p.id
    where vp.id = @id and p.id is not null;
    """

  [<Literal>]
  let UpdateVirtualProjectQuery =
    """
    update virtual_projects
    set
      connection = coalesce(@connection, connection),
      table_name = coalesce(@table_name, table_name),
      driver = coalesce(@driver, driver)
    where project_id = @project_id;
    """

  [<Literal>]
  let FindByVirtualMigrationName =
    """
    select
      vm.id as id,
      vm.name as name,
      vm.timestamp as timestamp,
      vm.up_content as up_content,
      vm.down_content as down_content,
      vm.virtual_project_id as virtual_project_id,
      vm.manual_transaction as manual_transaction
    from virtual_migrations as vm
    where vm.name = @name;
    """

  [<Literal>]
  let FindVirtualMigrationsByProjectId =
    """
    select
      vm.id as id,
      vm.name as name,
      vm.timestamp as timestamp,
      vm.up_content as up_content,
      vm.down_content as down_content,
      vm.virtual_project_id as virtual_project_id,
      vm.manual_transaction as manual_transaction
    from virtual_migrations as vm
    inner join virtual_projects as vp on vm.virtual_project_id = vp.id
    where vp.id = @project_id;
    """

  [<Literal>]
  let UpdateVirtualMigrationByName =
    """
    update virtual_migrations
    set
      up_content = coalesce(@up_content, up_content),
      down_content = coalesce(@down_content, down_content),
      manual_transaction = coalesce(@manual_transaction, manual_transaction)
    where name = @name;
    """

  [<Literal>]
  let InsertVirtualMigration =
    """
    insert into virtual_migrations (id, name, timestamp, up_content, down_content, virtual_project_id, manual_transaction)
    values (@id, @name, @timestamp, @up_content, @down_content, @virtual_project_id, @manual_transaction);
    """

  [<Literal>]
  let RemoveVirtualMigrationByName =
    """
    delete from virtual_migrations
    where name = @name;
    """

module Mappers =
  open Migrondi.Core

  let mapLocalProject readConfig (r: IDataReader) =
    let id = r.ReadGuid "id"
    let name = r.ReadString "name"
    let description = r.ReadStringOption "description"
    let configPath = r.ReadString "config_path"
    let config = readConfig configPath

    {
      id = id
      name = name
      description = description
      config = config
      migrondiConfigPath = configPath
    }

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

  let mapVirtualMigration(r: IDataReader) =
    let id = r.ReadGuid "id"
    let name = r.ReadString "name"
    let timestamp = r.ReadInt64 "timestamp"
    let upContent = r.ReadString "up_content"
    let downContent = r.ReadString "down_content"
    let virtualProjectId = r.ReadGuid "virtual_project_id"
    let manualTransaction = r.ReadBoolean "manual_transaction"

    {
      id = id
      name = name
      timestamp = timestamp
      upContent = upContent
      downContent = downContent
      projectId = virtualProjectId
      manualTransaction = manualTransaction
    }

module Database =
  open Microsoft.Data.Sqlite

  [<Struct>]
  type InsertLocalProjectArgs = {
    name: string
    description: string option
    configPath: string
  }

  [<Struct>]
  type UpdateProjectArgs = {
    id: Guid
    name: string
    description: string option
  }

  [<Struct>]
  type InsertVirtualProjectArgs = {
    name: string
    description: string option
    connection: string
    tableName: string
    driver: string
  }

  [<Struct>]
  type UpdateVirtualProjectArgs = {
    projectId: Guid
    connection: string
    tableName: string
    driver: string
  }

  [<Struct>]
  type UpdateVirtualMigrationArgs = {
    name: string
    upContent: string
    downContent: string
    manualTransaction: bool
  }

  [<Struct>]
  type InsertVirtualMigrationArgs = {
    name: string
    timestamp: int64
    upContent: string
    downContent: string
    virtualProjectId: Guid
    manualTransaction: bool
  }

  let ConnectionFactory() : IDbConnection =
    let dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "migrondi.db")
    let connectionString = $"Data Source={dbPath};"
    new SqliteConnection(connectionString)

  let FindLocalProjects(readConfig, createDbConnection: unit -> IDbConnection) =
    fun () -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.GetLocalProjects
        |> Db.setCancellationToken ct
        |> Db.Async.query(Mappers.mapLocalProject readConfig)
    }

  let FindLocalProjectById
    (readConfig, createDbConnection: unit -> IDbConnection)
    =
    fun (projectId: Guid) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.GetLocalProjectById
        |> Db.setCancellationToken ct
        |> Db.setParams [ "id", sqlString projectId ]
        |> Db.Async.querySingle(Mappers.mapLocalProject readConfig)
    }

  let InsertLocalProject(createDbConnection: unit -> IDbConnection) =
    fun (args: InsertLocalProjectArgs) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      let projectId = Guid.NewGuid()

      use! trx = connection.TryBeginTransactionAsync(ct)

      do!
        connection
        |> Db.newCommand Queries.InsertProject
        |> Db.setTransaction trx
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "id", sqlString projectId
          "name", sqlString args.name
          "description", sqlStringOrNull args.description
        ]
        |> Db.Async.exec

      do!
        connection
        |> Db.newCommand Queries.InsertLocalProject
        |> Db.setTransaction trx
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "id", sqlString(Guid.NewGuid())
          "config_path", sqlString args.configPath
          "project_id", sqlString projectId
        ]
        |> Db.Async.exec

      do! trx.TryCommitAsync(ct)
      return projectId
    }

  let UpdateProject(createDbConnection: unit -> IDbConnection) =
    fun (args: UpdateProjectArgs) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.UpdateProjectQuery
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "id", sqlString args.id
          "name", sqlString args.name
          "description", sqlStringOrNull args.description
        ]
        |> Db.Async.exec
    }

  let UpdateLocalProjectConfigPath(createDbConnection: unit -> IDbConnection) =
    fun (projectId: Guid, configPath: string) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.UpdateLocalProjectConfigPathQuery
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "project_id", sqlString projectId
          "config_path", sqlString configPath
        ]
        |> Db.Async.exec
    }

  // Virtual project database functions
  let FindVirtualProjects(createDbConnection: unit -> IDbConnection) =
    fun () -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.GetVirtualProjects
        |> Db.setCancellationToken ct
        |> Db.Async.query(Mappers.mapVirtualProject)
    }

  let FindVirtualProjectById(createDbConnection: unit -> IDbConnection) =
    fun (projectId: Guid) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.GetVirtualProjectById
        |> Db.setCancellationToken ct
        |> Db.setParams [ "id", sqlString projectId ]
        |> Db.Async.querySingle(Mappers.mapVirtualProject)
    }

  let InsertVirtualProject(createDbConnection: unit -> IDbConnection) =
    fun (args: InsertVirtualProjectArgs) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      let projectId = Guid.NewGuid()

      use! trx = connection.TryBeginTransactionAsync(ct)

      do!
        connection
        |> Db.newCommand Queries.InsertProject
        |> Db.setTransaction trx
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "id", sqlString projectId
          "name", sqlString args.name
          "description", sqlStringOrNull args.description
        ]
        |> Db.Async.exec

      do!
        connection
        |> Db.newCommand Queries.InsertVirtualProject
        |> Db.setTransaction trx
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "id", sqlString(Guid.NewGuid())
          "connection", sqlString args.connection
          "table_name", sqlString args.tableName
          "driver", sqlString args.driver
          "project_id", sqlString projectId
        ]
        |> Db.Async.exec

      do! trx.TryCommitAsync(ct)
      return projectId
    }

  let UpdateVirtualProject(createDbConnection: unit -> IDbConnection) =
    fun (args: UpdateVirtualProjectArgs) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use dbConnection = createDbConnection()

      do! dbConnection.TryOpenConnectionAsync(ct)

      return!
        dbConnection
        |> Db.newCommand Queries.UpdateVirtualProjectQuery
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "project_id", sqlString args.projectId
          "connection", sqlString args.connection
          "table_name", sqlString args.tableName
          "driver", sqlString args.driver
        ]
        |> Db.Async.exec
    }

  // Virtual migration database functions
  let FindVirtualMigrationByName(createDbConnection: unit -> IDbConnection) =
    fun (name: string) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.FindByVirtualMigrationName
        |> Db.setCancellationToken ct
        |> Db.setParams [ "name", sqlString name ]
        |> Db.Async.querySingle(Mappers.mapVirtualMigration)
    }

  let FindVirtualMigrationsByProjectId
    (createDbConnection: unit -> IDbConnection)
    =
    fun (projectId: Guid) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.FindVirtualMigrationsByProjectId
        |> Db.setCancellationToken ct
        |> Db.setParams [ "project_id", sqlString projectId ]
        |> Db.Async.query(Mappers.mapVirtualMigration)
    }

  let UpdateVirtualMigration(createDbConnection: unit -> IDbConnection) =
    fun (args: UpdateVirtualMigrationArgs) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.UpdateVirtualMigrationByName
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "name", sqlString args.name
          "up_content", sqlString args.upContent
          "down_content", sqlString args.downContent
          "manual_transaction", sqlBoolean args.manualTransaction
        ]
        |> Db.Async.exec
    }

  let InsertVirtualMigration(createDbConnection: unit -> IDbConnection) =
    fun (args: InsertVirtualMigrationArgs) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      let migrationId = Guid.NewGuid()

      do!
        connection
        |> Db.newCommand Queries.InsertVirtualMigration
        |> Db.setCancellationToken ct
        |> Db.setParams [
          "id", sqlString migrationId
          "name", sqlString args.name
          "timestamp", sqlInt64 args.timestamp
          "up_content", sqlString args.upContent
          "down_content", sqlString args.downContent
          "virtual_project_id", sqlString args.virtualProjectId
          "manual_transaction", sqlBoolean args.manualTransaction
        ]
        |> Db.Async.exec

      return migrationId
    }

  let RemoveVirtualMigrationByName(createDbConnection: unit -> IDbConnection) =
    fun (name: string) -> cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use connection = createDbConnection()

      do! connection.TryOpenConnectionAsync(ct)

      return!
        connection
        |> Db.newCommand Queries.RemoveVirtualMigrationByName
        |> Db.setCancellationToken ct
        |> Db.setParams [ "name", sqlString name ]
        |> Db.Async.exec
    }
