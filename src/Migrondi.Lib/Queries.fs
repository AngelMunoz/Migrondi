namespace Migrondi.Database

open System
open System.Collections
open System.Collections.Generic
open System.Data
open Microsoft.Data.SqlClient

open RepoDb
open RepoDb.Enumerations
open Npgsql
open MySql.Data.MySqlClient

open FsToolkit.ErrorHandling

open Migrondi.Types

/// A function that takes an ADO compatible connection string
/// A supported Driver
/// and returns a Lazy Connection, this connection might be reused by internal functions
/// so it's advisable to not close the connection until you're sure you will not use it anymore
type GetConnection = string -> Driver -> Lazy<IDbConnection>

/// A function that takes an optional migration and compare it with an existing migration file array
/// and return those migration files that have not been used yet (for either run up or down)
type GetMigrations = Migration option -> MigrationFile array -> MigrationFile array

/// Take a migration source and return the migration's name
type GetMigrationName = MigrationSource -> string

/// A function that fakes a migraion "run" for either Up or Down, return the migration name, the parameters and the content of said migration
type RunDryMigrations =
    Driver -> MigrationType -> MigrationFile array -> (string * IDictionary<string, obj> * string) array

/// A function that will perform a live run against the database for either Up or Down operations, return the array of database response (affected rows)
type RunMigrations = Driver -> IDbConnection -> MigrationType -> MigrationFile array -> Result<int array, exn>

/// A function used to ensure the "migration" table exists in the database
type TryEnsureMigrationsTableExists = Driver -> IDbConnection -> Result<unit, exn>

/// Check the database and retrieve the last migration in the "migration" table
type TryGetLastMigrationInDatabase = IDbConnection -> Result<Migration option, exn>

/// <summary>
/// RepoDB requires drivers to be initialized, check <see href="https://repodb.net/tutorial/installation#installation">RepoDB's Docs</see>
/// </summary>
type InitializeDriver = Driver -> unit

// Check if a particular migration name exists within the database
type TryGetByFilename = IDbConnection -> string -> Result<bool, exn>

[<RequireQualifiedAccess>]
module Queries =
    /// custom tuple box operator, takes a string that represents a column of a table
    /// and boxes the value on the right
    let inline private (=>) (column: string) (value: 'T) = column, box value

    /// gives the separator string used inside the migrations file
    let getSeparator (migrationType: MigrationType) (timestamp: int64) =
        let str =
            match migrationType with
            | MigrationType.Up -> "UP"
            | MigrationType.Down -> "DOWN"

        sprintf "-- ---------- MIGRONDI:%s:%i --------------" str timestamp

    let getMigrationName (migration: MigrationSource) =
        match migration with
        | MigrationSource.File migration -> sprintf "%s_%i.sql" migration.name migration.timestamp
        | MigrationSource.Database migration -> sprintf "%s_%i.sql" migration.name migration.timestamp

    let getPendingMigrations (lastMigration: Migration option) (migrations: MigrationFile array) =
        match lastMigration with
        | None -> migrations
        | Some migration ->
            let index =
                migrations
                |> Array.findIndex (fun m -> m.name = migration.name)

            match index + 1 = migrations.Length with
            | true -> Array.empty
            | false ->
                let pending = migrations |> Array.skip (index + 1)
                pending

    let getAppliedMigrations (lastMigration: Migration option) (migrations: MigrationFile array) =
        match lastMigration with
        | Some lastMigration ->
            let lastRanIndex =
                migrations
                |> Array.findIndex (fun m -> m.name = lastMigration.name)

            migrations.[0..lastRanIndex]
        | None -> Array.empty

    let getConnection: GetConnection =
        fun (connectionString: string) (driver: Driver) ->
            lazy
                (match driver with
                 | Driver.Mssql -> new SqlConnection(connectionString) :> IDbConnection
                 | Driver.Sqlite -> new SqlConnection(connectionString) :> IDbConnection
                 | Driver.Mysql -> new MySqlConnection(connectionString) :> IDbConnection
                 | Driver.Postgresql -> new NpgsqlConnection(connectionString) :> IDbConnection)

    let initializeDriver (driver: Driver) =
        let setup = GlobalConfiguration.Setup()

        match driver with
        | Driver.Mssql -> setup.UseMySql()
        | Driver.Sqlite -> setup.UseSqlite()
        | Driver.Mysql -> setup.UseMySql()
        | Driver.Postgresql -> setup.UsePostgreSql()
        |> ignore

    let private createTableQuery driver =
        match driver with
        | Driver.Sqlite ->
            """
            CREATE TABLE IF NOT EXISTS migration(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name VARCHAR(255) NOT NULL,
                timestamp BIGINT NOT NULL
            );
            """
        | Driver.Postgresql ->
            """
            CREATE TABLE IF NOT EXISTS migration(
               id SERIAL PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """
        | Driver.Mysql ->
            """
            CREATE TABLE IF NOT EXISTS migration(
               id INT AUTO_INCREMENT PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """
        | Driver.Mssql ->
            """
            CREATE TABLE IF NOT EXISTS dbo.migration(
               id INT PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """

    let createMigrationsTable (connection: IDbConnection) (driver: Driver) =
        result {
            try
                return
                    connection.ExecuteNonQuery(createTableQuery driver)
                    |> ignore
            with
            | :? Microsoft.Data.Sqlite.SqliteException as ex ->
                if ex.Message.Contains("already exists") then
                    return ()
                else
                    return! (Error(FailedToExecuteQuery ex.Message))
            | ex -> return! (Error(FailedToExecuteQuery ex.Message))
        }

    let getLastMigration (connection: IDbConnection) =
        result {
            let orderBy = seq { OrderField("timestamp", Order.Descending) }

            try
                let result = connection.QueryAll<Migration>("migration", orderBy = orderBy)

                return result |> Seq.tryHead
            with
            | :? Exceptions.MissingFieldsException -> return None
            | ex -> return! (Error(FailedToExecuteQuery ex.Message))
        }

    let migrationsTableExist (connection: IDbConnection) =
        result {
            try
                let! _ = getLastMigration connection
                return true
            with
            | ex -> return! (Error(FailedToExecuteQuery ex.Message))
        }

    let ensureMigrationsTable (driver: Driver) (connection: IDbConnection) = createMigrationsTable connection driver

    let private extractContent (migrationType: MigrationType) (migration: MigrationFile) =
        match migrationType with
        | MigrationType.Up -> migration.upContent
        | MigrationType.Down -> migration.downContent

    let private getInsertStatement (migrationType: MigrationType) =
        match migrationType with
        | MigrationType.Up -> "INSERT INTO migration(name, timestamp) VALUES(@Name, @Timestamp);"
        | MigrationType.Down -> "DELETE FROM migration WHERE timestamp = @Timestamp;"

    let private prepareMigrationContent (driver: Driver) (content: string) (insertStmn: string) =
        let (startStansaction, endtransaction) =
            match driver with
            | Driver.Mssql -> "BEGIN TRANSACTION;", "COMMIT TRANSACTION;"
            | Driver.Mysql -> "START TRANSACTION;", "COMMIT;"
            | Driver.Sqlite
            | Driver.Postgresql -> "BEGIN TRANSACTION;", "END TRANSACTION;"

        sprintf "%s%s\n%s\n%s" startStansaction content insertStmn endtransaction

    let private prepareQueryParams (migrationType: MigrationType) (migration: MigrationFile) =
        match migrationType with
        | MigrationType.Up ->
            [ "Name" => migration.name
              "Timestamp" => migration.timestamp ]
            |> Map.ofSeq
        | MigrationType.Down ->
            [ "Timestamp" => migration.timestamp ]
            |> Map.ofSeq
        :> IDictionary<string, obj>

    let private applyMigration
        (driver: Driver)
        (connection: IDbConnection)
        (migrationType: MigrationType)
        (migration: MigrationFile)
        =
        let content = extractContent migrationType migration
        let insert = getInsertStatement migrationType

        let migrationContent = prepareMigrationContent driver content insert

        let queryParams = prepareQueryParams migrationType migration

        try
            connection.ExecuteNonQuery(migrationContent, queryParams)
            |> Ok
        with
        | ex ->
            MigrationApplyFailedException(ex.Message, migration, driver)
            |> Error

    let runMigrations
        (driver: Driver)
        (connection: IDbConnection)
        (migrationType: MigrationType)
        (migrationFiles: array<MigrationFile>)
        =
        let applyMigrationWithConnectionAndType =
            applyMigration driver connection migrationType

        result {
            let results = ResizeArray()

            for file in migrationFiles do
                let! result = applyMigrationWithConnectionAndType file
                results.Add result

            return results |> Array.ofSeq
        }

    let dryRunMigrations
        (driver: Driver)
        (migrationType: MigrationType)
        (migrationFiles: array<MigrationFile>)
        : (string * IDictionary<string, obj> * string) array =
        let getMigrationContent (migration: MigrationFile) =
            let content = extractContent migrationType migration
            let insert = getInsertStatement migrationType

            let content = prepareMigrationContent driver content insert

            let queryParams = prepareQueryParams migrationType migration

            migration.name, queryParams, content

        if migrationType = MigrationType.Down then
            migrationFiles
            |> Array.sortBy (fun x -> x.timestamp)
            |> Array.map getMigrationContent
        else
            migrationFiles
            |> Array.sortByDescending (fun x -> x.timestamp)
            |> Array.map getMigrationContent

    let tryGetByFilename: TryGetByFilename =
        fun connection filename ->
            result {
                let! name, timestamp =
                    result {
                        let parts = filename.Split('_')

                        if parts.Length <> 2 then
                            return!
                                InvalidMigrationName
                                    $"{filename} is not a valid migration name, migration names come as \"NAME_TIMESTAMP.sql\""
                                |> Error

                        try
                            return parts.[0], (parts.[1].Split('.') |> Array.head) |> int64
                        with
                        | :? System.FormatException ->
                            return!
                                InvalidMigrationName
                                    $"The timestamp in this filename is not an integer or is missing: {filename}"
                                |> Error
                    }

                try
                    let result = connection.Count("migration", {| name = name; timestamp = timestamp |})

                    return result >= 1L
                with
                | :? RepoDb.Exceptions.MissingFieldsException -> return false
                | ex -> return! (Error(FailedToExecuteQuery ex.Message))
            }
