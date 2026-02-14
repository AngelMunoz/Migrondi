namespace Migrondi.Core.Database

open System
open System.Collections.Generic
open System.Data
open System.Data.Common

open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

open Microsoft.Extensions.Logging

open Migrondi.Core

open IcedTasks


[<Interface>]
type internal IMiDatabaseHandler =

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

  let getFirstResultQuery(tableName, driver) =
    match driver with
    | MigrondiDriver.Sqlite
    | MigrondiDriver.Postgresql
    | MigrondiDriver.Mysql ->
      $"SELECT id, name, timestamp FROM %s{tableName} ORDER BY timestamp DESC LIMIT 1"
    | MigrondiDriver.Mssql ->
      $"SELECT TOP 1 id, name, timestamp FROM %s{tableName} ORDER BY timestamp DESC"

  let getAllResultsQuery tableName =
    $"SELECT id, name, timestamp FROM {tableName} ORDER BY timestamp DESC"

  let getResultsByNamesQuery tableName (namePlaceholders: string) =
    $"SELECT id, name, timestamp FROM {tableName} WHERE name IN ({namePlaceholders}) ORDER BY timestamp DESC"

module MigrationsImpl =

  let getConnection(connectionString: string, driver: MigrondiDriver) =
    match driver with
    | MigrondiDriver.Mssql ->
      new Microsoft.Data.SqlClient.SqlConnection(connectionString)
      :> DbConnection
    | MigrondiDriver.Sqlite ->
      new Microsoft.Data.Sqlite.SqliteConnection(connectionString)
    | MigrondiDriver.Mysql ->
      new MySql.Data.MySqlClient.MySqlConnection(connectionString)
    | MigrondiDriver.Postgresql -> new Npgsql.NpgsqlConnection(connectionString)

  let setupDatabase
    (connection: DbConnection) // Changed IDbConnection to DbConnection
    (driver: MigrondiDriver)
    (tableName: string)
    =
    if connection.State <> ConnectionState.Open then
      connection.Open()

    try
      use command = connection.CreateCommand()
      command.CommandText <- Queries.createTable driver tableName
      command.ExecuteNonQuery() |> ignore
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


  let findMigration (connection: DbConnection) tableName name =

    use command = connection.CreateCommand()

    command.CommandText <-
      $"SELECT id, name, timestamp FROM {tableName} WHERE name = @name"

    let param = command.CreateParameter()
    param.ParameterName <- "@name"
    param.Value <- name
    command.Parameters.Add(param) |> ignore // Added |> ignore

    use reader = command.ExecuteReader()

    if reader.Read() then
      Some {
        id = reader.GetInt32(0) // Assuming id is int, adjust if different
        name = reader.GetString(1)
        timestamp = reader.GetInt64(2)
      }
    else
      None

  let findLastApplied (connection: DbConnection) driver tableName =
    use command = connection.CreateCommand()
    command.CommandText <- Queries.getFirstResultQuery(tableName, driver)

    use reader = command.ExecuteReader()

    if reader.Read() then
      Some {
        id = reader.GetInt32(0)
        name = reader.GetString(1)
        timestamp = reader.GetInt64(2)
      }
    else
      None

  let listMigrations
    (connection: DbConnection)
    (tableName: string)
    : MigrationRecord list = // Changed IDbConnection to DbConnection and return type
    use command = connection.CreateCommand()

    command.CommandText <-
      $"SELECT id, name, timestamp FROM {tableName} ORDER BY timestamp DESC"

    use reader = command.ExecuteReader()

    [ // Using list comprehension
      while reader.Read() do
        {
          id = reader.GetInt32(0)
          name = reader.GetString(1)
          timestamp = reader.GetInt64(2)
        }
    ]

  let runQuery
    (logger: ILogger, connection: DbConnection, tableName: string) // Changed to DbConnection
    (migration: Migration, content: string, isUp: bool)
    =
    logger.LogDebug(
      "Applying migration {Name} with content: {Content}",
      migration.name,
      content
    )

    if migration.manualTransaction then
      logger.LogDebug("Executing migration in manual transaction mode")

      try
        if connection.State <> ConnectionState.Open then
          connection.Open()

        use command = connection.CreateCommand()
        command.CommandText <- content
        command.ExecuteNonQuery() |> ignore

        if isUp then
          use insertCmd = connection.CreateCommand()

          insertCmd.CommandText <-
            $"INSERT INTO {tableName} (name, timestamp) VALUES (@name, @timestamp)"

          let nameParam = insertCmd.CreateParameter()
          nameParam.ParameterName <- "@name"
          nameParam.Value <- migration.name
          insertCmd.Parameters.Add(nameParam) |> ignore
          let tsParam = insertCmd.CreateParameter()
          tsParam.ParameterName <- "@timestamp"
          tsParam.Value <- migration.timestamp
          insertCmd.Parameters.Add(tsParam) |> ignore
          insertCmd.ExecuteNonQuery() |> ignore
        else
          use deleteCmd = connection.CreateCommand()
          deleteCmd.CommandText <- $"DELETE FROM {tableName} WHERE name = @name"
          let nameParam = deleteCmd.CreateParameter()
          nameParam.ParameterName <- "@name"
          nameParam.Value <- migration.name
          deleteCmd.Parameters.Add(nameParam) |> ignore
          deleteCmd.ExecuteNonQuery() |> ignore
      with ex ->
        logger.LogError(
          "Failed to execute migration {Name} due: {Message}",
          migration.name,
          ex.Message
        )

        if isUp then
          raise(MigrationApplicationFailed migration)
        else
          raise(MigrationRollbackFailed migration)
    else
      if connection.State <> ConnectionState.Open then
        connection.Open()

      use transaction = connection.BeginTransaction()

      try
        use command = connection.CreateCommand()
        command.CommandText <- content
        command.Transaction <- transaction // Assign transaction
        command.ExecuteNonQuery() |> ignore

        if isUp then
          use insertCmd = connection.CreateCommand()

          insertCmd.CommandText <-
            $"INSERT INTO {tableName} (name, timestamp) VALUES (@name, @timestamp)"

          insertCmd.Transaction <- transaction // Assign transaction
          let nameParam = insertCmd.CreateParameter()
          nameParam.ParameterName <- "@name"
          nameParam.Value <- migration.name
          insertCmd.Parameters.Add(nameParam) |> ignore
          let tsParam = insertCmd.CreateParameter()
          tsParam.ParameterName <- "@timestamp"
          tsParam.Value <- migration.timestamp
          insertCmd.Parameters.Add(tsParam) |> ignore
          insertCmd.ExecuteNonQuery() |> ignore
        else
          use deleteCmd = connection.CreateCommand()
          deleteCmd.CommandText <- $"DELETE FROM {tableName} WHERE name = @name"
          deleteCmd.Transaction <- transaction // Assign transaction
          let nameParam = deleteCmd.CreateParameter()
          nameParam.ParameterName <- "@name"
          nameParam.Value <- migration.name
          deleteCmd.Parameters.Add(nameParam) |> ignore
          deleteCmd.ExecuteNonQuery() |> ignore

        transaction.Commit()
      with ex ->
        logger.LogError(
          "Failed to execute migration {Name} due: {Message}",
          migration.name,
          ex.Message
        )

        transaction.Rollback()

        if isUp then
          raise(MigrationApplicationFailed migration)
        else
          raise(MigrationRollbackFailed migration)

  let applyMigrations
    (connection: DbConnection) // Changed to DbConnection
    (logger: ILogger)
    tableName
    (migrations: Migration list)
    =
    let executeMigration = runQuery(logger, connection, tableName)

    for migration in migrations do
      let content = migration.upContent

      if String.IsNullOrWhiteSpace(content) then
        logger.LogError(
          "Migration {Name} does not have an up migration, the migration process will stop here.",
          migration.name
        )

        raise(MigrationApplicationFailed migration)

      executeMigration(migration, content, true)

    // Replace RepoDB QueryAll with ADO.NET
    use command = connection.CreateCommand()

    command.CommandText <-
      $"SELECT id, name, timestamp FROM {tableName} ORDER BY timestamp DESC"

    use reader = command.ExecuteReader()

    [ // Using list comprehension
      while reader.Read() do
        {
          id = reader.GetInt32(0)
          name = reader.GetString(1)
          timestamp = reader.GetInt64(2)
        }
    ]

  let rollbackMigrations
    (connection: DbConnection) // Changed to DbConnection
    (logger: ILogger)
    tableName
    (migrations: Migration list)
    =
    let executeMigration = runQuery(logger, connection, tableName)

    let rolledBack =
      let migrationNames = migrations |> List.map(_.name)

      if List.isEmpty migrationNames then
        []
      else
        use command = connection.CreateCommand()

        let namePlaceholders =
          String.Join(",", migrationNames |> List.mapi(fun i _ -> $"@name{i}"))

        command.CommandText <-
          Queries.getResultsByNamesQuery tableName namePlaceholders

        migrationNames
        |> List.iteri(fun i name ->
          let param = command.CreateParameter()
          param.ParameterName <- $"@name{i}"
          param.Value <- name
          command.Parameters.Add(param) |> ignore)

        use reader = command.ExecuteReader()

        [
          while reader.Read() do
            {
              id = reader.GetInt32(0)
              name = reader.GetString(1)
              timestamp = reader.GetInt64(2)
            }
        ]


    for migration in migrations do
      let content = migration.downContent

      if String.IsNullOrWhiteSpace(content) then
        logger.LogError(
          "Migration {Name} does not have a down migration, the rollback process will stop here.",
          migration.name
        )

        raise(MigrationRollbackFailed migration)

      executeMigration(migration, content, false)

    rolledBack |> Seq.toList

module MigrationsAsyncImpl =

  let setupDatabaseAsync
    (connection: DbConnection) // Changed IDbConnection to DbConnection
    (driver: MigrondiDriver)
    (tableName: string)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()

      try
        use command = connection.CreateCommand()
        command.CommandText <- Queries.createTable driver tableName
        do! command.ExecuteNonQueryAsync(token) :> Task

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

  let findMigrationAsync (connection: DbConnection) tableName name = cancellableTask { // Changed signature
    let! token = CancellableTask.getCancellationToken() // Get token from context
    use command = connection.CreateCommand()

    command.CommandText <-
      $"SELECT id, name, timestamp FROM %s{tableName} WHERE name = @name"

    let param = command.CreateParameter()
    param.ParameterName <- "@name"
    param.Value <- name
    command.Parameters.Add(param) |> ignore

    use! reader = command.ExecuteReaderAsync(token) // Use token
    let! result = reader.ReadAsync(token) // Use token

    if result then
      return
        Some {
          id = reader.GetInt32(0)
          name = reader.GetString(1)
          timestamp = reader.GetInt64(2)
        }
    else
      return None
  }

  let findLastAppliedAsync (connection: DbConnection) tableName = cancellableTask { // Changed signature
    let! token = CancellableTask.getCancellationToken() // Get token from context
    use command = connection.CreateCommand()

    command.CommandText <-
      $"SELECT id, name, timestamp FROM %s{tableName} ORDER BY timestamp DESC LIMIT 1"

    use! reader = command.ExecuteReaderAsync(token) // Use token

    if reader.Read() then
      return
        Some {
          id = reader.GetInt32(0)
          name = reader.GetString(1)
          timestamp = reader.GetInt64(2)
        }
    else
      return None
  }

  let listMigrationsAsync (connection: DbConnection) (tableName: string) = cancellableTask { // Changed signature
    let! token = CancellableTask.getCancellationToken() // Get token from context
    use command = connection.CreateCommand()

    command.CommandText <-
      $"SELECT id, name, timestamp FROM %s{tableName} ORDER BY timestamp DESC"

    use! reader = command.ExecuteReaderAsync(token) // Use token

    return [ // Using list comprehension
      while reader.Read() do
        {
          id = reader.GetInt32(0)
          name = reader.GetString(1)
          timestamp = reader.GetInt64(2)
        }
    ]
  }

  let runQueryAsync
    (logger: ILogger, connection: DbConnection, tableName: string) // Changed to DbConnection
    (migration: Migration, content: string, isUp: bool)
    =
    cancellableTask {
      logger.LogDebug(
        "Applying migration {Name} with content: {Content}",
        migration.name,
        content
      )

      let! token = CancellableTask.getCancellationToken()

      if migration.manualTransaction then
        logger.LogDebug("Executing migration in manual transaction mode")

        try
          if connection.State <> ConnectionState.Open then
            do! connection.OpenAsync(token)

          use command = connection.CreateCommand()
          command.CommandText <- content
          do! command.ExecuteNonQueryAsync(token) :> Task

          if isUp then
            use insertCmd = connection.CreateCommand()

            insertCmd.CommandText <-
              $"INSERT INTO {tableName} (name, timestamp) VALUES (@name, @timestamp)"

            let nameParam = insertCmd.CreateParameter()
            nameParam.ParameterName <- "@name"
            nameParam.Value <- migration.name
            insertCmd.Parameters.Add(nameParam) |> ignore
            let tsParam = insertCmd.CreateParameter()
            tsParam.ParameterName <- "@timestamp"
            tsParam.Value <- migration.timestamp
            insertCmd.Parameters.Add(tsParam) |> ignore
            do! insertCmd.ExecuteNonQueryAsync(token) :> Task
            return ()
          else
            use deleteCmd = connection.CreateCommand()

            deleteCmd.CommandText <-
              $"DELETE FROM {tableName} WHERE name = @name"

            let nameParam = deleteCmd.CreateParameter()
            nameParam.ParameterName <- "@name"
            nameParam.Value <- migration.name
            deleteCmd.Parameters.Add(nameParam) |> ignore
            do! deleteCmd.ExecuteNonQueryAsync(token) :> Task
            return ()
        with ex ->
          logger.LogError(
            "Failed to execute migration {Name} due: {Message}",
            migration.name,
            ex.Message
          )

          if isUp then
            raise(MigrationApplicationFailed migration)
          else
            raise(MigrationRollbackFailed migration)
      else
        if connection.State <> ConnectionState.Open then
          do! connection.OpenAsync(token)
        // BeginTransaction is synchronous.
        use! transaction = connection.BeginTransactionAsync(token)

        try
          use command = connection.CreateCommand()
          command.CommandText <- content
          command.Transaction <- transaction
          do! command.ExecuteNonQueryAsync(token) :> Task

          if isUp then
            use insertCmd = connection.CreateCommand()

            insertCmd.CommandText <-
              $"INSERT INTO {tableName} (name, timestamp) VALUES (@name, @timestamp)"

            insertCmd.Transaction <- transaction
            let nameParam = insertCmd.CreateParameter()
            nameParam.ParameterName <- "@name"
            nameParam.Value <- migration.name
            insertCmd.Parameters.Add(nameParam) |> ignore
            let tsParam = insertCmd.CreateParameter()
            tsParam.ParameterName <- "@timestamp"
            tsParam.Value <- migration.timestamp
            insertCmd.Parameters.Add(tsParam) |> ignore
            do! insertCmd.ExecuteNonQueryAsync(token) :> Task
            ()
          else
            use deleteCmd = connection.CreateCommand()

            deleteCmd.CommandText <-
              $"DELETE FROM {tableName} WHERE name = @name"

            deleteCmd.Transaction <- transaction
            let nameParam = deleteCmd.CreateParameter()
            nameParam.ParameterName <- "@name"
            nameParam.Value <- migration.name
            deleteCmd.Parameters.Add(nameParam) |> ignore
            do! deleteCmd.ExecuteNonQueryAsync(token) :> Task
            ()

          do! transaction.CommitAsync(token)
          return ()
        with ex ->
          logger.LogError(
            "Failed to execute migration {Name} due: {Message}",
            migration.name,
            ex.Message
          )

          do! transaction.RollbackAsync(token)

          if isUp then
            raise(MigrationApplicationFailed migration)
          else
            raise(MigrationRollbackFailed migration)
    }

  let applyMigrationsAsync
    (connection: DbConnection) // Changed to DbConnection
    (logger: ILogger)
    tableName
    (migrations: Migration list)
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let executeMigration = runQueryAsync(logger, connection, tableName)

      for migration in migrations do
        let content = migration.upContent

        if String.IsNullOrWhiteSpace(content) then
          logger.LogError(
            "Migration {Name} does not have an up migration, the migration process will stop here.",
            migration.name
          )

          raise(MigrationApplicationFailed migration)

        do! executeMigration(migration, content, true)

      // Replace RepoDB QueryAllAsync with ADO.NET
      use command = connection.CreateCommand()

      command.CommandText <- Queries.getAllResultsQuery tableName

      use! reader = command.ExecuteReaderAsync(token)

      return [ // Using list comprehension
        while reader.Read() do
          {
            id = reader.GetInt32(0)
            name = reader.GetString(1)
            timestamp = reader.GetInt64(2)
          }
      ]
    }

  let rollbackMigrationsAsync
    (connection: DbConnection) // Changed to DbConnection
    (logger: ILogger)
    tableName
    (migrationsToRollback: Migration list) // Renamed for clarity
    =
    cancellableTask {
      let! token = CancellableTask.getCancellationToken()
      let executeMigration = runQueryAsync(logger, connection, tableName)

      // Get the records from DB *before* rolling them back to return them
      let migrationNames = migrationsToRollback |> List.map(_.name)
      let mutable rolledBackRecords = [] // Default to empty list

      if not(List.isEmpty migrationNames) then
        use command = connection.CreateCommand()

        let namePlaceholders =
          String.Join(",", migrationNames |> List.mapi(fun i _ -> $"@name{i}"))

        command.CommandText <-
          Queries.getResultsByNamesQuery tableName namePlaceholders

        migrationNames
        |> List.iteri(fun i name ->
          let param = command.CreateParameter()
          param.ParameterName <- $"@name{i}"
          param.Value <- name
          command.Parameters.Add(param) |> ignore)

        use! reader = command.ExecuteReaderAsync(token)

        rolledBackRecords <- [
          while reader.Read() do
            {
              id = reader.GetInt32(0)
              name = reader.GetString(1)
              timestamp = reader.GetInt64(2)
            }
        ]

      for migration in migrationsToRollback do
        let content = migration.downContent

        if String.IsNullOrWhiteSpace(content) then
          logger.LogError(
            "Migration {Name} does not have a down migration, the rollback process will stop here.",
            migration.name
          )

          raise(MigrationRollbackFailed migration)

        do! executeMigration(migration, content, false)

      return rolledBackRecords // Return the records that were present before rollback
    }

[<Class>]
type internal MiDatabaseHandler(logger: ILogger, config: MigrondiConfig) =

  interface IMiDatabaseHandler with

    member _.SetupDatabase() =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      if connection.State <> ConnectionState.Open then
        connection.Open()

      MigrationsImpl.setupDatabase connection config.driver config.tableName

    member _.FindLastApplied() : MigrationRecord option =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      if connection.State <> ConnectionState.Open then
        connection.Open()

      MigrationsImpl.findLastApplied connection config.driver config.tableName

    member _.ApplyMigrations(migrations: Migration seq) =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      if connection.State <> ConnectionState.Open then
        connection.Open()

      migrations
      |> Seq.toList
      |> MigrationsImpl.applyMigrations connection logger config.tableName
      :> MigrationRecord IReadOnlyList

    member _.FindMigration(name: string) : MigrationRecord option =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      if connection.State <> ConnectionState.Open then
        connection.Open()

      MigrationsImpl.findMigration connection config.tableName name

    member _.ListMigrations() =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      if connection.State <> ConnectionState.Open then
        connection.Open()

      MigrationsImpl.listMigrations connection config.tableName
      :> MigrationRecord IReadOnlyList // Added cast to IReadOnlyList

    member _.RollbackMigrations(migrations: Migration seq) =
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      if connection.State <> ConnectionState.Open then
        connection.Open()

      migrations
      |> Seq.toList
      |> MigrationsImpl.rollbackMigrations connection logger config.tableName
      :> MigrationRecord IReadOnlyList

    member _.FindLastAppliedAsync([<Optional>] ?cancellationToken) = task {
      // Token for the outer task block is implicitly passed to cancellableTask
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      let token = defaultArg cancellationToken CancellationToken.None

      if connection.State <> ConnectionState.Open then
        do! connection.OpenAsync(token)

      return!
        MigrationsAsyncImpl.findLastAppliedAsync
          connection
          config.tableName
          token
    }

    member _.ApplyMigrationsAsync
      (migrations: Migration seq, [<Optional>] ?cancellationToken)
      =
      task {
        let token = defaultArg cancellationToken CancellationToken.None

        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        if connection.State <> ConnectionState.Open then
          do! connection.OpenAsync(token)

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
      (migrations: Migration seq, [<Optional>] ?cancellationToken)
      =
      task {
        let token = defaultArg cancellationToken CancellationToken.None

        use connection =
          MigrationsImpl.getConnection(config.connection, config.driver)

        if connection.State <> ConnectionState.Open then
          do! connection.OpenAsync(token)

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

    member _.SetupDatabaseAsync([<Optional>] ?cancellationToken) = task {

      let token = defaultArg cancellationToken CancellationToken.None

      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      if connection.State <> ConnectionState.Open then
        do! connection.OpenAsync(token)

      return!
        MigrationsAsyncImpl.setupDatabaseAsync
          connection
          config.driver
          config.tableName
          token
    }


    member _.FindMigrationAsync(name: string, [<Optional>] ?cancellationToken) = task {

      // Token for the outer task block is implicitly passed to cancellableTask
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      let token = defaultArg cancellationToken CancellationToken.None

      if connection.State <> ConnectionState.Open then
        do! connection.OpenAsync(token)

      return!
        MigrationsAsyncImpl.findMigrationAsync
          connection
          config.tableName
          name
          token
    }

    member _.ListMigrationsAsync([<Optional>] ?cancellationToken) = task {
      // Token for the outer task block is implicitly passed to cancellableTask
      use connection =
        MigrationsImpl.getConnection(config.connection, config.driver)

      let token = defaultArg cancellationToken CancellationToken.None

      if connection.State <> ConnectionState.Open then
        do! connection.OpenAsync(token)

      let! result =
        MigrationsAsyncImpl.listMigrationsAsync
          connection
          config.tableName
          token

      return result :> IReadOnlyList<MigrationRecord>
    }
