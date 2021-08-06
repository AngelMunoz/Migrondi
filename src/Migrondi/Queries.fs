namespace Migrondi

open System
open System.Data
open System.Data.SQLite
open System.Data.SqlClient

open RepoDb
open RepoDb.Enumerations
open Npgsql
open MySql.Data.MySqlClient

open FsToolkit.ErrorHandling

open Migrondi.Types

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

    let migrationName (migration: MigrationSource) =
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

    let getConnection (connectionString: string) (driver: Driver) : Lazy<IDbConnection> =
        lazy
            (match driver with
             | Driver.Mssql -> new SqlConnection(connectionString) :> IDbConnection
             | Driver.Sqlite -> new SQLiteConnection(connectionString) :> IDbConnection
             | Driver.Mysql -> new MySqlConnection(connectionString) :> IDbConnection
             | Driver.Postgresql -> new NpgsqlConnection(connectionString) :> IDbConnection)

    let initializeDriver (driver: Driver) =
        match driver with
        | Driver.Mssql -> RepoDb.SqlServerBootstrap.Initialize()
        | Driver.Sqlite -> RepoDb.SqLiteBootstrap.Initialize()
        | Driver.Mysql -> RepoDb.MySqlBootstrap.Initialize()
        | Driver.Postgresql -> RepoDb.PostgreSqlBootstrap.Initialize()

    let private createTableQuery driver =
        match driver with
        | Driver.Sqlite ->
            """
            CREATE TABLE migration(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name VARCHAR(255) NOT NULL,
                timestamp BIGINT NOT NULL
            );
            """
        | Driver.Postgresql ->
            """
            CREATE TABLE migration(
               id SERIAL PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """
        | Driver.Mysql ->
            """
            CREATE TABLE migration(
               id INT AUTO_INCREMENT PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """
        | Driver.Mssql ->
            """
            CREATE TABLE dbo.migration(
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
            | ex -> return! (Error(FailedToExecuteQuery ex.Message))
        }

    let getLastMigration (connection: IDbConnection) =
        result {
            let orderBy =
                seq { OrderField("timestamp", Order.Descending) }

            try
                let result =
                    connection.QueryAll<Migration>("migration", orderBy = orderBy)

                return result |> Seq.tryHead
            with
            | ex -> return! (Error(FailedToExecuteQuery ex.Message))
        }

    let migrationsTableExist (connection: IDbConnection) =
        result {
            try
                let! migration = getLastMigration connection
                return migration |> Option.isSome
            with
            | ex -> return! (Error(FailedToExecuteQuery ex.Message))
        }

    let ensureMigrationsTable (driver: Driver) (connection: IDbConnection) =
        result {
            let! tableExists = migrationsTableExist connection

            if not <| tableExists then
                do! createMigrationsTable connection driver
        }

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

    let private applyMigration
        (driver: Driver)
        (connection: IDbConnection)
        (migrationType: MigrationType)
        (migration: MigrationFile)
        =
        let content = extractContent migrationType migration
        let insert = getInsertStatement migrationType

        let migrationContent =
            prepareMigrationContent driver content insert

        let queryParams =
            prepareQueryParams migrationType migration

        try
            connection.ExecuteNonQuery(migrationContent, queryParams)
        with
        | ex -> raise (MigrationApplyFailedException(ex.Message, migration, driver))

    let runMigrations
        (driver: Driver)
        (connection: IDbConnection)
        (migrationType: MigrationType)
        (migrationFiles: array<MigrationFile>)
        =
        let applyMigrationWithConnectionAndType =
            applyMigration driver connection migrationType

        migrationFiles
        |> Array.map applyMigrationWithConnectionAndType

    let dryRunMigrations
        (driver: Driver)
        (migrationType: MigrationType)
        (migrationFiles: array<MigrationFile>)
        : (string * Map<string, obj> * string) array =
        let getMigrationContent (migration: MigrationFile) =
            let content = extractContent migrationType migration
            let insert = getInsertStatement migrationType

            let content =
                prepareMigrationContent driver content insert

            let queryParams =
                prepareQueryParams migrationType migration

            migration.name, queryParams, content

        migrationFiles |> Array.map getMigrationContent
