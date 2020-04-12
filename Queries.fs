namespace Migrondi

open System.Collections.Generic
open System.Data
open RepoDb
open RepoDb.Enumerations
open Types
open Utils.Operators

module Queries =
    let private createTableQuery driver =
        match driver with
        | Driver.Sqlite -> """
            CREATE TABLE migration(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name VARCHAR(255) NOT NULL,
                timestamp BIGINT NOT NULL
            );
            """
        | Driver.Postgresql -> """
            CREATE TABLE migration(
               id SERIAL PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """
        | Driver.Mysql -> """
            CREATE TABLE migration(
               id INT AUTO_INCREMENT PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """
        | Driver.Mssql -> """
            CREATE TABLE dbo.migration(
               id INT PRIMARY KEY,
               name VARCHAR(255) NOT NULL,
               timestamp BIGINT NOT NULL
            );
            """

    let createMigrationsTable (connection: IDbConnection) (driver: Driver) =
        try
            connection.ExecuteNonQuery(createTableQuery driver) |> ignore
            true
        with ex ->
            printfn "%s" ex.Message
            false

    let getLastMigration (connection: IDbConnection) =
        let orderBy: IEnumerable<OrderField> = seq { OrderField("timestamp", Order.Descending) }
        let result = connection.QueryAll<Migration>(orderBy = orderBy)
        result |> Seq.tryHead

    let migrationsTableExist (connection: IDbConnection) =
        try
            getLastMigration connection |> ignore
            true
        with ex -> false

    let ensureMigrationsTable (driver: Driver) (connection: IDbConnection) =
        let tableExists = migrationsTableExist connection
        match tableExists with
        | false -> createMigrationsTable connection driver
        | true -> true

    let private applyMigration
        (driver: Driver)
        (connection: IDbConnection)
        (migrationType: MigrationType)
        (migration: MigrationFile)
        =
        let content =
            match migrationType with
            | MigrationType.Up -> migration.upContent
            | MigrationType.Down -> migration.downContent

        let insertStmn =
            match migrationType with
            | MigrationType.Up -> "INSERT INTO migration(name, timestamp) VALUES(@Name, @Timestamp);"
            | MigrationType.Down -> "DELETE FROM migration WHERE timestamp = @Timestamp;"

        let migrationContent =
            let (startStansaction, endtransaction) =
                match driver with
                | Driver.Mssql -> "BEGIN TRANSACTION;", "COMMIT TRANSACTION;"
                | Driver.Mysql -> "START TRANSACTION;", "COMMIT;"
                | Driver.Sqlite
                | Driver.Postgresql -> "BEGIN TRANSACTION;", "END TRANSACTION;"
            sprintf "%s%s\n%s\n%s" startStansaction content insertStmn endtransaction

        let queryParams =
            match migrationType with
            | MigrationType.Up ->
                dict
                    [ "Name" => migration.name
                      "Timestamp" => migration.timestamp ]
            | MigrationType.Down -> dict [ "Timestamp" => migration.timestamp ]

        try
            connection.ExecuteNonQuery(migrationContent, queryParams)
        with ex ->
            printfn "Error while running migration \"%s\"" migration.name
            failwith ex.Message

    let runMigrations
        (driver: Driver)
        (connection: IDbConnection)
        (migrationType: MigrationType)
        (migrationFiles: array<MigrationFile>)
        =
        let applyMigrationWithConnectionAndType = applyMigration driver connection migrationType

        migrationFiles |> Array.map applyMigrationWithConnectionAndType
