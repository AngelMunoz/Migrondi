namespace MigrondiUI

open System
open System.Data
open Donald

module Queries =
  [<Literal>]
  let GetLocalProjects =
    """
    select
      p.id as id, p.name as name, p.description as description, lp.config_path as config_path
    from projects as p
    left join local_projects as lp
      on
      lp.project_id = p.id;
    """

  [<Literal>]
  let GetLocalProjectById =
    """
    select
      p.id as id, p.name as name, p.description as description, lp.config_path as config_path
    from projects as p
    left join local_projects as lp
      on
      lp.project_id = p.id
    where p.id = @id;
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

module Mappers =
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
    }

module Database =
  open Microsoft.Data.Sqlite

  let ConnectionFactory() : IDbConnection =
    let dbPath = System.IO.Path.Combine(AppContext.BaseDirectory, "migrondi.db")
    let connectionString = $"Data Source={dbPath};"
    new SqliteConnection(connectionString)

  let FindLocalProjects(readConfig, createDbConnection: unit -> IDbConnection) =
    fun () ->
      use connection = createDbConnection()
      connection.TryOpenConnection()

      connection
      |> Db.newCommand Queries.GetLocalProjects
      |> Db.Async.query(Mappers.mapLocalProject readConfig)

  let FindLocalProjectById
    (readConfig, createDbConnection: unit -> IDbConnection)
    =
    fun (projectId: Guid) ->
      use connection = createDbConnection()
      connection.TryOpenConnection()

      connection
      |> Db.newCommand Queries.GetLocalProjectById
      |> Db.setParams [ "id", sqlString projectId ]
      |> Db.Async.querySingle(Mappers.mapLocalProject readConfig)

  let InsertLocalProject(createDbConnection: unit -> IDbConnection) =
    fun (name: string, description: string option, configPath: string) -> task {
      use connection = createDbConnection()
      connection.TryOpenConnection()
      let projectId = Guid.NewGuid()

      do!
        connection
        |> Db.newCommand Queries.InsertProject
        |> Db.setParams [
          "id", sqlString projectId
          "name", sqlString name
          "description", sqlStringOrNull description
        ]
        |> Db.Async.exec

      do!
        connection
        |> Db.newCommand Queries.InsertLocalProject
        |> Db.setParams [
          "id", sqlString(Guid.NewGuid())
          "config_path", sqlString configPath
          "project_id", sqlString projectId
        ]
        |> Db.Async.exec

      return projectId
    }

  let UpdateProject(createDbConnection: unit -> IDbConnection) =
    fun (id: Guid, name: string, description: string option) ->
      use connection = createDbConnection()
      connection.TryOpenConnection()

      connection
      |> Db.newCommand Queries.UpdateProjectQuery
      |> Db.setParams [
        "id", sqlString id
        "name", sqlString name
        "description", sqlStringOrNull description
      ]
      |> Db.Async.exec

  let UpdateLocalProjectConfigPath(createDbConnection: unit -> IDbConnection) =
    fun (projectId: Guid, configPath: string) ->
      use connection = createDbConnection()
      connection.TryOpenConnection()

      connection
      |> Db.newCommand Queries.UpdateLocalProjectConfigPathQuery
      |> Db.setParams [
        "project_id", sqlString projectId
        "config_path", sqlString configPath
      ]
      |> Db.Async.exec
