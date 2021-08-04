namespace Migrondi

open System.Data
open RepoDb
open RepoDb.Enumerations
open Migrondi.Types

module Queries =
    /// custom tuple box operator, takes a string that represents a column of a table
    /// and boxes the value on the right
    let inline private (=>) (column: string) (value: 'T) = column, box value

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
        try
            connection.ExecuteNonQuery(createTableQuery driver)
            |> ignore

            true
        with
        | ex -> false

    let getLastMigration (connection: IDbConnection) =
        let orderBy =
            seq { OrderField("timestamp", Order.Descending) }

        let result =
            connection.QueryAll<Migration>("migration", orderBy = orderBy)

        result |> Seq.tryHead

    let migrationsTableExist (connection: IDbConnection) =
        try
            getLastMigration connection |> ignore
            true
        with
        | ex -> false

    let ensureMigrationsTable (driver: Driver) (connection: IDbConnection) =
        let tableExists = migrationsTableExist connection

        match tableExists with
        | false -> createMigrationsTable connection driver
        | true -> true

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
            dict [ "Name" => migration.name
                   "Timestamp" => migration.timestamp ]
        | MigrationType.Down -> dict [ "Timestamp" => migration.timestamp ]

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

    let private applyDryRunMigration (driver: Driver) (migrationType: MigrationType) (migration: MigrationFile) =
        let content = extractContent migrationType migration
        let insert = getInsertStatement migrationType

        let migrationContent =
            prepareMigrationContent driver content insert

        let queryParams =
            prepareQueryParams migrationType migration

        $"[MIGRATION: %s{migration.name}] - [PARAMS: %A{queryParams}]\n%s{migrationContent}"

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

    let dryRunMigrations (driver: Driver) (migrationType: MigrationType) (migrationFiles: array<MigrationFile>) =
        let applyMigrationWithConnectionAndType =
            applyDryRunMigration driver migrationType

        let migrations =
            migrationFiles
            |> Array.map applyMigrationWithConnectionAndType

        printfn "%s" (System.String.Join('\n', migrations))
        Array.create migrations.Length 1
