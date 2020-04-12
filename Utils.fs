namespace Migrondi

open Types
open System.Data.SQLite
open System.Data.SqlClient
open System.IO
open System.Text.Json
open System
open System.Data
open Npgsql
open MySql.Data.MySqlClient

module internal Utils =
    module Operators =
        /// custom tuple box operator, takes a string that represents a column of a table
        /// and boxes the value on the right
        let inline (=>) (column: string) (value: 'T) = column, box value

    /// gives the separator string used inside the migrations file
    let getSeparator (migrationType: MigrationType) (timestamp: int64) =
        let str =
            match migrationType with
            | MigrationType.Up -> "UP"
            | MigrationType.Down -> "DOWN"
        sprintf "-- ---------- MIGRONDI:%s:%i --------------" str timestamp


    let createNewMigrationFile (path: string) (name: string) =
        let timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        let name = sprintf "%s_%i.sql" (name.Trim()) timestamp
        let fullpath = Path.Combine(path, name)

        let contentBytes =
            let content =
                sprintf "-- ---------- MIGRONDI:UP:%i --------------\n-- Write your Up migrations here\n\n" timestamp
                + (sprintf "-- ---------- MIGRONDI:DOWN:%i --------------\n-- Write how to revert the migration here"
                       timestamp)

            Text.Encoding.UTF8.GetBytes(content)

        let file = File.Create(fullpath)

        file, contentBytes


    /// Gets the configuration for the execution of a command
    /// <exception cref="FileNotFoundException">
    /// Thrown when the "migrondi.json" configuration file is not found
    /// </exception
    let getMigrondiConfiguration() =
        let dir = Directory.GetCurrentDirectory()
        let info = DirectoryInfo(dir)
        let file = info.EnumerateFiles() |> Seq.tryFind (fun (f: FileInfo) -> f.Name = "migrondi.json")

        let content =
            match file with
            | Some file -> File.ReadAllText(file.FullName)
            | None -> raise (FileNotFoundException "migrondi.json file not found, aborting.")

        let config = JsonSerializer.Deserialize<MigrondiConfig>(content)

        match config.driver with
        | "mssql"
        | "sqlite"
        | "postgres"
        | "mysql" -> config
        | others ->
            let drivers = "mssql | sqlite | postgres | mysql"
            raise
                (ArgumentException
                    (sprintf "The driver selected \"%s\" does not match the available drivers  %s" others drivers))

    let getPathConfigAndDriver() =
        let config = getMigrondiConfiguration()
        let path = Path.GetDirectoryName config.migrationsDir
        let driver = Driver.FromString config.driver
        if String.IsNullOrEmpty path then
            raise
                (ArgumentException
                    "Path seems to be empty, please check that you have provided the correct path to your migrations directory")
        path, config, driver


    let getMigrations (path: string) =
        let dir = DirectoryInfo(path)

        let fileMapping (file: FileInfo) =
            let name = file.Name
            let reader = file.OpenText()
            let content = reader.ReadToEnd()
            reader.Close()
            let split = name.Split('_')
            let filenameErr =
                sprintf "File \"%s\" is not a valid migration name. The migration name should look like %s" name
                    "[NAME]_[TIMESTAMP].sql example: NAME_0123456789.sql"

            let (name, timestamp) =
                match split.Length = 2 with
                | true ->
                    let secondSplit = split.[1].Split(".")
                    match secondSplit.Length = 2 with
                    | true -> split.[0], (secondSplit.[0] |> int64)
                    | false -> failwith filenameErr
                | false -> failwith filenameErr

            let getSplitContent migrationType =
                let separator = getSeparator migrationType timestamp
                content.Split(separator)

            let splitContent = getSplitContent MigrationType.Down

            let upContent, downContent =
                match splitContent.Length = 2 with
                | true -> splitContent.[0], splitContent.[1]
                | false ->
                    failwith
                        "The migration file does not contain UP, Down sql statements or it contains more than one section of one of those."

            { name = name
              timestamp = timestamp
              upContent = upContent
              downContent = downContent }

        dir.EnumerateFiles()
        |> Seq.filter (fun f -> f.Extension = ".sql")
        |> Seq.toArray
        |> Array.Parallel.map fileMapping
        |> Array.sortBy (fun m -> m.timestamp)

    let migrationName (migration: MigrationSource) =
        match migration with
        | MigrationSource.File migration -> sprintf "%s_%i.sql" migration.name migration.timestamp
        | MigrationSource.Database migration -> sprintf "%s_%i.sql" migration.name migration.timestamp

    let getPendingMigrations (lastMigration: Migration option) (migrations: MigrationFile array) =
        match lastMigration with
        | None -> migrations
        | Some migration ->
            let index = migrations |> Array.findIndex (fun m -> m.name = migration.name)
            match index + 1 = migrations.Length with
            | true -> Array.empty
            | false ->
                let pending = migrations |> Array.skip (index + 1)
                pending

    let getAppliedMigrations (lastMigration: Migration option) (migrations: MigrationFile array) =
        match lastMigration with
        | Some lastMigration ->
            let lastRanIndex = migrations |> Array.findIndex (fun m -> m.name = lastMigration.name)
            migrations.[0..lastRanIndex]
        | None -> Array.empty

    /// gets a new sqlite connection based on the connection string
    let getConnection (driver: Driver) (connectionString: string): IDbConnection =
        match driver with
        | Driver.Mssql -> new SqlConnection(connectionString) :> IDbConnection
        | Driver.Sqlite -> new SQLiteConnection(connectionString) :> IDbConnection
        | Driver.Mysql -> new MySqlConnection(connectionString) :> IDbConnection
        | Driver.Postgresql -> new NpgsqlConnection(connectionString) :> IDbConnection
