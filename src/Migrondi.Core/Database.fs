namespace Migrondi.Core.Database

open System
open System.Data
open Microsoft.Data.SqlClient
open Microsoft.Data.Sqlite
open MySqlConnector
open Npgsql


open FsToolkit.ErrorHandling

open Migrondi.Core
open System.Threading
open System.Threading.Tasks


[<AutoOpen>]
module Exceptions =
  open System.Runtime.ExceptionServices

  let inline reriseCustom<'ReturnValue> (exn: exn) =
    ExceptionDispatchInfo.Capture(exn).Throw()
    Unchecked.defaultof<'ReturnValue>

  exception MigrationApplicationFailed of Migration
  exception MigrationRollbackFailed of Migration
  exception SetupDatabaseFailed

[<Interface>]
type DatabaseEnv =

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabase: unit -> unit

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigration: name: string -> MigrationRecord option

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastApplied: unit -> MigrationRecord option

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrations: unit -> MigrationRecord list

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were applied to the database
  /// </returns>
  /// <remarks>
  /// This Method will throw an <see cref="MigrationApplicationFailed"/> exception if
  /// it fails to apply a migration
  /// </remarks>
  abstract member ApplyMigrations:
    migrations: Migration list -> MigrationRecord list

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  /// <remarks>
  /// This Method will throw an <see cref="MigrationRollbackFailed"/> exception if
  /// it fails to rollback a migration
  /// </remarks>
  abstract member RollbackMigrations:
    migrations: Migration list -> MigrationRecord list

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabaseAsync:
    ?cancellationToken: CancellationToken -> Task

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <param name="cancellationToken">A cancellation token</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigrationAsync:
    name: string * ?cancellationToken: CancellationToken ->
      Task<MigrationRecord option>

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastAppliedAsync:
    ?cancellationToken: CancellationToken -> Task<MigrationRecord option>

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrationsAsync:
    ?cancellationToken: CancellationToken -> Task<MigrationRecord list>

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were applied to the database
  /// </returns>
  abstract member ApplyMigrationsAsync:
    migrations: Migration list * ?cancellationToken: CancellationToken ->
      Task<MigrationRecord list>

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  abstract member RollbackMigrationsAsync:
    migrations: Migration list * ?cancellationToken: CancellationToken ->
      Task<MigrationRecord list>

[<RequireQualifiedAccess>]
module Queries =
  let createTable driver tableName =
    match driver with
    | MigrondiDriver.Sqlite ->
      $"""CREATE TABLE IF NOT EXISTS %s{tableName}(
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name VARCHAR(255) NOT NULL,
  timestamp BIGINT NOT NULL
);"""
    | MigrondiDriver.Postgresql ->
      $"""CREATE TABLE IF NOT EXISTS %s{tableName}(
  id SERIAL PRIMARY KEY,
  name VARCHAR(255) NOT NULL,
  timestamp BIGINT NOT NULL
);"""
    | MigrondiDriver.Mysql ->
      $"""CREATE TABLE IF NOT EXISTS %s{tableName}(
  id INT AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(255) NOT NULL,
  timestamp BIGINT NOT NULL
);"""
    | MigrondiDriver.Mssql ->
      $"""IF OBJECT_ID(N'dbo.{tableName}', N'U') IS NULL
CREATE TABLE dbo.%s{tableName}(
  id INT PRIMARY KEY,
  name VARCHAR(255) NOT NULL,
  timestamp BIGINT NOT NULL
);
GO"""


module MigrationsImpl =
  open RepoDb

  let inline private (=>) (a: string) b = a, box b

  let getConnection (connectionString: string, driver: MigrondiDriver) =
    match driver with
    | MigrondiDriver.Mssql ->
      new SqlConnection(connectionString) :> IDbConnection
    | MigrondiDriver.Sqlite -> new SqliteConnection(connectionString)
    | MigrondiDriver.Mysql -> new MySqlConnection(connectionString)
    | MigrondiDriver.Postgresql -> new NpgsqlConnection(connectionString)

  let initializeDriver (driver: MigrondiDriver) =
    let setup = GlobalConfiguration.Setup()

    match driver with
    | MigrondiDriver.Sqlite -> setup.UseSqlite()
    | MigrondiDriver.Mssql -> setup.UseSqlServer()
    | MigrondiDriver.Postgresql -> setup.UsePostgreSql()
    | MigrondiDriver.Mysql -> setup.UseMySqlConnector()
    |> ignore

  let setupDatabase
    (connection: IDbConnection)
    (driver: MigrondiDriver)
    (tableName: string)
    =
    initializeDriver driver

    try
      connection.ExecuteNonQuery(Queries.createTable driver tableName) |> ignore
    with ex ->
      if
        ex.Message.Contains(
          "already exists",
          StringComparison.InvariantCultureIgnoreCase
        )
      then
        ()
      else
        reriseCustom(SetupDatabaseFailed)


  let findMigration (connection: IDbConnection) tableName name =
    let queryParams = QueryGroup([ QueryField("name", name) ])

    connection.Query<MigrationRecord>(
      tableName = tableName,
      where = queryParams
    )
    |> Seq.tryHead

  let findLastApplied (connection: IDbConnection) tableName =

    connection.QueryAll<MigrationRecord>(
      tableName = tableName,
      orderBy = [
        OrderField.Descending(fun (record: MigrationRecord) -> record.timestamp)
      ]
    )
    |> Seq.tryHead

  let listMigrations (connection: IDbConnection) (tableName: string) =
    connection.QueryAll<MigrationRecord>(
      tableName = tableName,
      orderBy = [
        OrderField.Descending(fun (record: MigrationRecord) -> record.timestamp)
      ]
    )
    |> Seq.toList

  let applyMigrations
    (connection: IDbConnection)
    tableName
    (migrations: Migration list)
    =
    for migration in migrations do
      let content = migration.upContent
      use transaction = connection.EnsureOpen().BeginTransaction()

      try

        // execute the user's migration
        connection.ExecuteNonQuery($"{content};;", transaction = transaction)
        |> ignore

        // Insert the tracking record after the user's migration was executed
        connection.Insert(
          tableName = tableName,
          entity = {|
            name = migration.name
            timestamp = migration.timestamp
          |},
          fields = Field.From("name", "timestamp"),
          transaction = transaction
        )
        |> ignore

        transaction.Commit()
      with _ ->
        transaction.Rollback()
        reriseCustom(MigrationApplicationFailed migration)

    connection.QueryAll<MigrationRecord>(
      tableName = tableName,
      orderBy = [
        OrderField.Descending(fun (record: MigrationRecord) -> record.timestamp)
      ]
    )
    |> Seq.toList

  let rollbackMigrations
    (connection: IDbConnection)
    tableName
    (migrations: Migration list)
    =
    for migration in migrations do
      use transaction = connection.EnsureOpen().BeginTransaction()

      try
        let content = migration.downContent

        // Rollback the migration
        connection.ExecuteNonQuery($"{content};;", transaction = transaction)
        |> ignore

        // Remove the existing MigrationRecord that represented this migration
        connection.Delete(
          tableName = tableName,
          where = QueryGroup([ QueryField("timestamp", migration.timestamp) ]),
          transaction = transaction
        )
        |> ignore

        transaction.Commit()
      with _ ->
        transaction.Rollback()
        reriseCustom(MigrationRollbackFailed migration)

    connection.QueryAll<MigrationRecord>(
      tableName = tableName,
      orderBy = [
        OrderField.Descending(fun (record: MigrationRecord) -> record.timestamp)
      ]
    )
    |> Seq.toList

module MigrationsAsyncImpl =
  open RepoDb

  let setupDatabaseAsync
    (connection: IDbConnection)
    (driver: MigrondiDriver)
    (tableName: string)
    =
    async {
      let! token = Async.CancellationToken
      MigrationsImpl.initializeDriver driver

      try
        do!
          connection.ExecuteNonQueryAsync(
            Queries.createTable driver tableName,
            cancellationToken = token
          )
          |> Async.AwaitTask
          |> Async.Ignore

        return ()
      with ex ->
        if
          ex.Message.Contains(
            "already exists",
            StringComparison.InvariantCultureIgnoreCase
          )
        then
          return ()
        else
          reriseCustom(SetupDatabaseFailed)
    }

  let findMigrationAsync (connection: IDbConnection) tableName name = async {
    let! token = Async.CancellationToken
    let queryParams = QueryGroup([ QueryField("name", name) ])

    let! result =
      connection.QueryAsync<MigrationRecord>(
        tableName = tableName,
        where = queryParams,
        cancellationToken = token
      )
      |> Async.AwaitTask

    return result |> Seq.tryHead
  }

  let findLastAppliedAsync (connection: IDbConnection) tableName = async {
    let! token = Async.CancellationToken

    let! result =
      connection.QueryAllAsync<MigrationRecord>(
        tableName = tableName,
        orderBy = [
          OrderField.Descending(fun (record: MigrationRecord) ->
            record.timestamp
          )
        ],
        cancellationToken = token
      )
      |> Async.AwaitTask

    return result |> Seq.tryHead
  }

  let listMigrationsAsync (connection: IDbConnection) (tableName: string) = async {
    let! token = Async.CancellationToken

    let! result =
      connection.QueryAllAsync<MigrationRecord>(
        tableName = tableName,
        orderBy = [
          OrderField.Descending(fun (record: MigrationRecord) ->
            record.timestamp
          )
        ],
        cancellationToken = token
      )
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let applyMigrationsAsync
    (connection: IDbConnection)
    tableName
    (migrations: Migration list)
    =
    async {
      let! token = Async.CancellationToken

      for migration in migrations do
        use transaction = connection.EnsureOpen().BeginTransaction()
        let content = migration.upContent

        try
          do!
            // execute the user's migration
            connection.ExecuteNonQueryAsync(
              $"{content};;",
              transaction = transaction,
              cancellationToken = token
            )
            |> Async.AwaitTask
            |> Async.Ignore

          do!
            // Insert the tracking record after the user's migration was executed
            connection.InsertAsync(
              tableName = tableName,
              entity = {|
                name = migration.name
                timestamp = migration.timestamp
              |},
              fields = Field.From("name", "timestamp"),
              transaction = transaction,
              cancellationToken = token
            )
            |> Async.AwaitTask
            |> Async.Ignore

          transaction.Commit()
        with _ ->
          transaction.Rollback()
          raise(MigrationApplicationFailed migration)

      let! result =
        connection.QueryAllAsync<MigrationRecord>(
          tableName = tableName,
          orderBy = [
            OrderField.Descending(fun (record: MigrationRecord) ->
              record.timestamp
            )
          ],
          cancellationToken = token
        )
        |> Async.AwaitTask

      return result |> Seq.toList
    }

  let rollbackMigrationsAsync
    (connection: IDbConnection)
    tableName
    (migrations: Migration list)
    =
    async {
      let! token = Async.CancellationToken

      for migration in migrations do
        use transaction = connection.EnsureOpen().BeginTransaction()
        let content = migration.downContent

        try
          do!
            // Rollback the migration
            connection.ExecuteNonQueryAsync(
              $"{content};;",
              transaction = transaction,
              cancellationToken = token
            )
            |> Async.AwaitTask
            |> Async.Ignore

          do!
            // Remove the existing MigrationRecord that represented this migration
            connection.DeleteAsync(
              tableName = tableName,
              where =
                QueryGroup([ QueryField("timestamp", migration.timestamp) ]),
              transaction = transaction,
              cancellationToken = token
            )
            |> Async.AwaitTask
            |> Async.Ignore

          transaction.Commit()
        with ex ->
          transaction.Rollback()
          raise(MigrationRollbackFailed migration)

      let! result =
        connection.QueryAllAsync<MigrationRecord>(
          tableName = tableName,
          orderBy = [
            OrderField.Descending(fun (record: MigrationRecord) ->
              record.timestamp
            )
          ],
          cancellationToken = token
        )
        |> Async.AwaitTask

      return result |> Seq.toList
    }

[<Class>]
type DatabaseImpl =

  static member Build(config: MigrondiConfig) =
    { new DatabaseEnv with

        member _.SetupDatabase() =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.setupDatabase connection config.driver config.tableName

        member _.FindLastApplied() : MigrationRecord option =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.findLastApplied connection config.tableName

        member _.ApplyMigrations(migrations: Migration list) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.applyMigrations connection config.tableName migrations

        member _.FindMigration(name: string) : MigrationRecord option =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.findMigration connection config.tableName name

        member _.ListMigrations() : MigrationRecord list =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.listMigrations connection config.tableName

        member _.RollbackMigrations(migrations: Migration list) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.rollbackMigrations
            connection
            config.tableName
            migrations

        member _.FindLastAppliedAsync(?cancellationToken) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          let computation =
            MigrationsAsyncImpl.findLastAppliedAsync connection config.tableName

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)


        member _.ApplyMigrationsAsync
          (
            migrations: Migration list,
            ?cancellationToken
          ) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          let computation =
            MigrationsAsyncImpl.applyMigrationsAsync
              connection
              config.tableName
              migrations

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

        member _.RollbackMigrationsAsync
          (
            migrations: Migration list,
            ?cancellationToken
          ) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          let computation =
            MigrationsAsyncImpl.rollbackMigrationsAsync
              connection
              config.tableName
              migrations

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

        member _.SetupDatabaseAsync(?cancellationToken) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          let computation =
            MigrationsAsyncImpl.setupDatabaseAsync
              connection
              config.driver
              config.tableName

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

        member _.FindMigrationAsync(name: string, ?cancellationToken) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          let computation =
            MigrationsAsyncImpl.findMigrationAsync
              connection
              config.tableName
              name

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

        member _.ListMigrationsAsync(?cancellationToken) =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          let computation =
            MigrationsAsyncImpl.listMigrationsAsync connection config.tableName

          Async.StartAsTask(computation, ?cancellationToken = cancellationToken)

    }