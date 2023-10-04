namespace Migrondi.Core.Database

open System
open System.Collections.Generic
open System.Data
open Microsoft.Data.SqlClient
open Microsoft.Data.Sqlite
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

open MySqlConnector
open Npgsql

open RepoDb
open RepoDb.Enumerations
open Microsoft.Extensions.Logging

open FsToolkit.ErrorHandling

open Migrondi.Core

open IcedTasks


[<Interface>]
type IMiDatabaseHandler =

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  /// <exception cref="Migrondi.Core.Database.Exceptions.SetupDatabaseFailed">
  /// Thrown when the setup of the database failed
  /// </exception>
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
  abstract member ListMigrations: unit -> MigrationRecord IReadOnlyList

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
    migrations: Migration seq -> MigrationRecord IReadOnlyList

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
    migrations: Migration seq -> MigrationRecord IReadOnlyList

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabaseAsync:
    [<Optional>] ?cancellationToken: CancellationToken -> Task

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <param name="cancellationToken">A cancellation token</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigrationAsync:
    name: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationRecord option>

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastAppliedAsync:
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationRecord option>

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrationsAsync:
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationRecord IReadOnlyList>

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were applied to the database
  /// </returns>
  abstract member ApplyMigrationsAsync:
    migrations: Migration seq *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationRecord IReadOnlyList>

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  abstract member RollbackMigrationsAsync:
    migrations: Migration seq *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrationRecord IReadOnlyList>


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

module internal MigrationsImpl =

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
    (logger: ILogger)
    tableName
    (migrations: Migration list)
    =
    for migration in migrations do
      let content = migration.upContent
      use transaction = connection.EnsureOpen().BeginTransaction()

      try
        logger.LogDebug(
          "Applying migration {Name} with content: {Content}",
          migration.name,
          content
        )
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
      with ex ->
        logger.LogDebug("Failed to apply migration due: {Message}", ex.Message)
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
    (logger: ILogger)
    tableName
    (migrations: Migration list)
    =

    let rolledBack =
      connection.Query<MigrationRecord>(
        tableName = tableName,
        where =
          QueryField(
            "name",
            Operation.In,
            migrations |> Seq.map(fun m -> m.name)
          ),
        orderBy = [
          OrderField.Descending(fun (record: MigrationRecord) ->
            record.timestamp
          )
        ]
      )

    for migration in migrations do
      use transaction = connection.EnsureOpen().BeginTransaction()

      try
        let content = migration.downContent

        logger.LogDebug(
          "Rolling back migration {Name} with content: {Content}",
          migration.name,
          content
        )

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
      with ex ->
        logger.LogDebug(
          "Failed to rollback migration due: {Message}",
          ex.Message
        )

        transaction.Rollback()
        reriseCustom(MigrationRollbackFailed migration)

    rolledBack |> Seq.toList

module MigrationsAsyncImpl =

  let setupDatabaseAsync
    (connection: IDbConnection)
    (driver: MigrondiDriver)
    (tableName: string)
    =
    cancellableTask {
      MigrationsImpl.initializeDriver driver
      let! token = CancellableTask.getCancellationToken()

      try
        let! _ =
          connection.ExecuteNonQueryAsync(
            Queries.createTable driver tableName,
            cancellationToken = token
          )

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

  let findMigrationAsync (connection: IDbConnection) tableName name = cancellableTask {
    let! token = CancellableTask.getCancellationToken()
    let queryParams = QueryGroup([ QueryField("name", name) ])

    let! result =
      connection.QueryAsync<MigrationRecord>(
        tableName = tableName,
        where = queryParams,
        cancellationToken = token
      )

    return result |> Seq.tryHead
  }

  let findLastAppliedAsync (connection: IDbConnection) tableName = cancellableTask {
    let! token = CancellableTask.getCancellationToken()

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

    return result |> Seq.tryHead
  }

  let listMigrationsAsync (connection: IDbConnection) (tableName: string) = cancellableTask {
    let! token = CancellableTask.getCancellationToken()

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

    return result |> Seq.toList
  }

  let applyMigrationsAsync
    (connection: IDbConnection)
    (logger: ILogger)
    tableName
    (migrations: Migration list)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()

      for migration in migrations do
        use transaction = connection.EnsureOpen().BeginTransaction()
        let content = migration.upContent

        try
          logger.LogDebug(
            "Applying migration {Name} with content: {Content}",
            migration.name,
            content
          )

          let! _ =
            // execute the user's migration
            connection.ExecuteNonQueryAsync(
              $"{content};;",
              transaction = transaction,
              cancellationToken = token
            )

          let! _ =
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

          transaction.Commit()
        with ex ->
          logger.LogDebug(
            "Failed to apply migration due: {Message}",
            ex.Message
          )

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

      return result |> Seq.toList
    }

  let rollbackMigrationsAsync
    (connection: IDbConnection)
    (logger: ILogger)
    tableName
    (migrations: Migration list)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()

      for migration in migrations do
        use transaction = connection.EnsureOpen().BeginTransaction()
        let content = migration.downContent

        try

          logger.LogDebug(
            "Rolling back migration {Name} with content: {Content}",
            migration.name,
            content
          )

          let! _ =
            // Rollback the migration
            connection.ExecuteNonQueryAsync(
              $"{content};;",
              transaction = transaction,
              cancellationToken = token
            )

          let! _ =
            // Remove the existing MigrationRecord that represented this migration
            connection.DeleteAsync(
              tableName = tableName,
              where =
                QueryGroup([ QueryField("timestamp", migration.timestamp) ]),
              transaction = transaction,
              cancellationToken = token
            )

          transaction.Commit()
        with ex ->
          logger.LogDebug(
            "Failed to rollback migration due: {Message}",
            ex.Message
          )

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

      return result |> Seq.toList
    }

[<Class>]
type MiDatabaseHandler(logger: ILogger, config: MigrondiConfig) =

  interface IMiDatabaseHandler with

    member _.SetupDatabase() =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      MigrationsImpl.setupDatabase connection config.driver config.tableName

    member _.FindLastApplied() : MigrationRecord option =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      MigrationsImpl.findLastApplied connection config.tableName

    member _.ApplyMigrations(migrations: Migration seq) =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      migrations
      |> Seq.toList
      |> MigrationsImpl.applyMigrations connection logger config.tableName
      :> MigrationRecord IReadOnlyList

    member _.FindMigration(name: string) : MigrationRecord option =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      MigrationsImpl.findMigration connection config.tableName name

    member _.ListMigrations() =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      MigrationsImpl.listMigrations connection config.tableName

    member _.RollbackMigrations(migrations: Migration seq) =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      migrations
      |> Seq.toList
      |> MigrationsImpl.rollbackMigrations connection logger config.tableName
      :> MigrationRecord IReadOnlyList

    member _.FindLastAppliedAsync([<Optional>] ?cancellationToken) =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      let token = defaultArg cancellationToken CancellationToken.None

      MigrationsAsyncImpl.findLastAppliedAsync connection config.tableName token


    member _.ApplyMigrationsAsync
      (
        migrations: Migration seq,
        [<Optional>] ?cancellationToken
      ) =
      task {
        let token = defaultArg cancellationToken CancellationToken.None

        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        let computation =
          migrations
          |> Seq.toList
          |> MigrationsAsyncImpl.applyMigrationsAsync
            connection
            logger
            config.tableName

        let! result = computation token
        return result :> IReadOnlyList<MigrationRecord>
      }

    member _.RollbackMigrationsAsync
      (
        migrations: Migration seq,
        [<Optional>] ?cancellationToken
      ) =
      task {
        let token = defaultArg cancellationToken CancellationToken.None

        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        let computation =
          migrations
          |> Seq.toList
          |> MigrationsAsyncImpl.rollbackMigrationsAsync
            connection
            logger
            config.tableName

        let! result = computation token
        return result :> IReadOnlyList<MigrationRecord>
      }

    member _.SetupDatabaseAsync([<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)


      MigrationsAsyncImpl.setupDatabaseAsync
        connection
        config.driver
        config.tableName
        token


    member _.FindMigrationAsync(name: string, [<Optional>] ?cancellationToken) =
      let token = defaultArg cancellationToken CancellationToken.None

      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      MigrationsAsyncImpl.findMigrationAsync
        connection
        config.tableName
        name
        token

    member _.ListMigrationsAsync([<Optional>] ?cancellationToken) = task {
      let token = defaultArg cancellationToken CancellationToken.None

      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      let! result =
        MigrationsAsyncImpl.listMigrationsAsync
          connection
          config.tableName
          token

      return result :> IReadOnlyList<MigrationRecord>
    }