namespace Migrondi.Core.Database

open System
open System.Data
open Microsoft.Data.SqlClient
open Microsoft.Data.Sqlite
open MySqlConnector
open Npgsql


open FsToolkit.ErrorHandling

open Migrondi.Core

[<Interface>]
type DatabaseEnv =

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabase: unit -> Result<unit, string>

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
  abstract member ApplyMigrations:
    migrations: Migration list -> Result<MigrationRecord list, string>

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  abstract member RollbackMigrations:
    migrations: Migration list -> Result<MigrationRecord list, string>

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabaseAsync: unit -> Async<Result<unit, string>>

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigrationAsync:
    name: string -> Async<MigrationRecord option>

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastAppliedAsync: unit -> Async<MigrationRecord option>

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrationsAsync: unit -> Async<MigrationRecord list>

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were applied to the database
  /// </returns>
  abstract member ApplyMigrationsAsync:
    migrations: Migration list -> Async<Result<MigrationRecord list, string>>

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  abstract member RollbackMigrationsAsync:
    migrations: Migration list -> Async<Result<MigrationRecord list, string>>

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

  let insertMigrationRecord tableName =
    $"INSERT INTO %s{tableName}(name, timestamp) VALUES(@__Name, @__Timestamp);"

  let deleteMigrationRecord tableName =
    $"DELETE FROM %s{tableName} WHERE timestamp = @__Timestamp;"

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
      connection.ExecuteNonQuery(Queries.createTable driver tableName)
      |> Result.requireEqualTo 0 "Failed to create migrations table"
    with ex ->
      if
        ex.Message.Contains(
          "already exists",
          StringComparison.InvariantCultureIgnoreCase
        )
      then
        Ok()
      else
        Error ex.Message


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
    result {
      for migration in migrations do
        let content = migration.upContent
        use transaction = connection.EnsureOpen().BeginTransaction()

        try

          // execute the user's migration
          connection.ExecuteNonQuery(content, transaction = transaction)
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
          transaction.Rollback()
          return! Error ex.Message

      return
        connection.QueryAll<MigrationRecord>(
          tableName = tableName,
          orderBy = [
            OrderField.Descending(fun (record: MigrationRecord) ->
              record.timestamp
            )
          ]
        )
        |> Seq.toList
    }

  let rollbackMigrations
    (connection: IDbConnection)
    tableName
    (migrations: Migration list)
    =
    result {
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
        with ex ->
          transaction.Rollback()
          return! Error ex.Message

      return
        connection.QueryAll<MigrationRecord>(
          tableName = tableName,
          orderBy = [
            OrderField.Descending(fun (record: MigrationRecord) ->
              record.timestamp
            )
          ]
        )
        |> Seq.toList
    }

module MigrationsAsyncImpl =
  open RepoDb
  open FsToolkit.ErrorHandling

  let inline private (=>) (a: string) b = a, box b

  let setupDatabaseAsync
    (connection: IDbConnection)
    (driver: MigrondiDriver)
    (tableName: string)
    =
    asyncResult {
      MigrationsImpl.initializeDriver driver

      try
        let! _ =
          connection.ExecuteNonQueryAsync(Queries.createTable driver tableName)

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
          return! Error ex.Message
    }

  let findMigrationAsync (connection: IDbConnection) tableName name = async {
    let! result =
      connection.QueryAsync<MigrationRecord>(
        tableName,
        fun value -> value.name = name
      )
      |> Async.AwaitTask

    return result |> Seq.tryHead
  }

  let findLastAppliedAsync (connection: IDbConnection) tableName = async {
    let! result =
      connection.QueryAllAsync<MigrationRecord>(
        tableName = tableName,
        orderBy = [
          OrderField.Descending(fun (record: MigrationRecord) ->
            record.timestamp
          )
        ]
      )
      |> Async.AwaitTask

    return result |> Seq.tryHead
  }

  let listMigrationsAsync (connection: IDbConnection) (tableName: string) = async {
    let! result =
      connection.QueryAllAsync<MigrationRecord>(
        tableName = tableName,
        orderBy = [
          OrderField.Descending(fun (record: MigrationRecord) ->
            record.timestamp
          )
        ]
      )
      |> Async.AwaitTask

    return result |> Seq.toList
  }

  let applyMigrationsAsync
    (connection: IDbConnection)
    tableName
    (migrations: Migration list)
    =
    asyncResult {
      use transaction = connection.BeginTransaction()

      try
        for migration in migrations do
          let insert = Queries.deleteMigrationRecord tableName
          let content = migration.upContent

          let param =
            [ "__Name" => migration.name; "__Timestamp" => migration.timestamp ]
            |> dict

          do!
            connection.ExecuteNonQueryAsync(
              $"{content};;\n{insert};;",
              param = param,
              transaction = transaction
            )
            |> Async.AwaitTask
            |> Async.Ignore


        transaction.Commit()

        let orderBy = [
          OrderField.Descending(fun (record: MigrationRecord) ->
            record.timestamp
          )
        ]

        let! result =
          connection.QueryAsync<MigrationRecord>(tableName, orderBy = orderBy)
          |> Async.AwaitTask

        return result |> Seq.toList
      with ex ->
        transaction.Rollback()
        return! Error ex.Message
    }

  let rollbackMigrationsAsync
    (connection: IDbConnection)
    tableName
    (migrations: Migration list)
    =
    asyncResult {
      use transaction = connection.BeginTransaction()

      try
        for migration in migrations do
          let deleteRecord = Queries.deleteMigrationRecord tableName
          let content = migration.downContent

          let param = [ "__Timestamp" => migration.timestamp ] |> dict

          do!
            connection.ExecuteNonQueryAsync(
              $"{content};;\n{deleteRecord};;",
              param = param,
              transaction = transaction
            )
            |> Async.AwaitTask
            |> Async.Ignore

        transaction.Commit()

        let orderBy = [
          OrderField.Descending(fun (record: MigrationRecord) ->
            record.timestamp
          )
        ]

        let! result =
          connection.QueryAsync<MigrationRecord>(tableName, orderBy = orderBy)

        return result |> Seq.toList
      with ex ->
        transaction.Rollback()
        return! Error ex.Message
    }

[<Class>]
type DatabaseImpl =

  static member Build(config: MigrondiConfig) =
    { new DatabaseEnv with

        member _.SetupDatabase() : Result<unit, string> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.setupDatabase connection config.driver config.tableName

        member _.FindLastApplied() : MigrationRecord option =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.findLastApplied connection config.tableName

        member _.ApplyMigrations
          (migrations: Migration list)
          : Result<MigrationRecord list, string> =
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

        member _.RollbackMigrations
          (migrations: Migration list)
          : Result<MigrationRecord list, string> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsImpl.rollbackMigrations
            connection
            config.tableName
            migrations

        member _.FindLastAppliedAsync() : Async<MigrationRecord option> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsAsyncImpl.findLastAppliedAsync connection config.tableName

        member _.ApplyMigrationsAsync
          (migrations: Migration list)
          : Async<Result<MigrationRecord list, string>> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsAsyncImpl.applyMigrationsAsync
            connection
            config.tableName
            migrations

        member _.RollbackMigrationsAsync
          (migrations: Migration list)
          : Async<Result<MigrationRecord list, string>> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsAsyncImpl.applyMigrationsAsync
            connection
            config.tableName
            migrations

        member _.SetupDatabaseAsync() : Async<Result<unit, string>> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsAsyncImpl.setupDatabaseAsync
            connection
            config.driver
            config.tableName

        member _.FindMigrationAsync
          (name: string)
          : Async<MigrationRecord option> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsAsyncImpl.findMigrationAsync
            connection
            config.tableName
            name

        member _.ListMigrationsAsync() : Async<MigrationRecord list> =
          use connection =
            MigrationsImpl.getConnection(config.connection, config.driver)

          MigrationsAsyncImpl.listMigrationsAsync connection config.tableName

    }