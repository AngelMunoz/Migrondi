namespace Sqlator

open System
open System.IO
open Dapper
open FSharp.Data.Dapper
open Types
open Options
open System.Text.Json
open System.Data.SQLite

module Migrations =
    let inline private (=>) (column: string) (value: 'T) = column, box value

    let private getSeparator (migrationType: MigrationType) (timestamp: int64) =
        let str =
            match migrationType with
            | MigrationType.Up -> "UP"
            | MigrationType.Down -> "DOWN"
        sprintf "------------ SQLATOR:%s:%i --------------" str timestamp

    let private getConnection (connStr: string) = SqliteConnection(new SQLiteConnection(connStr))

    let private getSqlatorConfiguration() =
        let dir = Directory.GetCurrentDirectory()
        let info = DirectoryInfo(dir)
        let file = info.EnumerateFiles() |> Seq.tryFind (fun (f: FileInfo) -> f.Name = "sqlator.json")

        let content =
            match file with
            | Some file -> File.ReadAllText(file.FullName)
            | None -> failwith "sqlator.json file not found, aborting."

        JsonSerializer.Deserialize<SqlatorConfig>(content)

    let private getMigrations (path: string) =
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


    let private doesMigrationsTableExists (connection: Connection) =

        async {
            let query =
                querySeqAsync<Migration> (fun () -> connection)
                    { script "Select * FROM Migrations ORDER BY Date DESC LIMIT 1;" }
            let! getMigrationRecords = Async.Catch query
            return match getMigrationRecords with
                   | Choice1Of2 result -> true
                   | Choice2Of2 exn ->
                       printfn "%s" exn.Message
                       false
        }

    let private createMigrationsTable (connection: Connection) =
        let query =
            querySingleOptionAsync (fun () -> connection) {
                script """
            CREATE TABLE Migrations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(255) NOT NULL,
                Date INTEGER NOT NULL);
            """
            }
        Async.Catch query

    let private ensureMigrationsTableExists (constr: string) =
        let connection = getConnection constr

        let tableExistsOrCreated (tableExists: Async<bool>) =
            let connection = getConnection constr
            async {
                let! tableExists = tableExists
                match tableExists with
                | false ->
                    let! result = createMigrationsTable connection
                    return match result with
                           | Choice1Of2 result -> true
                           | Choice2Of2 exn ->
                               printfn "%s" exn.Message
                               false
                | true -> return true
            }
        tableExistsOrCreated (doesMigrationsTableExists connection)

    let private vMigration (migration: MigrationFile) (migrationType: MigrationType) (connection: string) =
        let connection = getConnection connection

        let content =
            match migrationType with
            | MigrationType.Up -> migration.upContent
            | MigrationType.Down -> migration.downContent

        let insertStmn =
            match migrationType with
            | MigrationType.Up -> "INSERT INTO Migrations (Name, Date) VALUES(@Name, @Date);"
            | MigrationType.Down -> "DELETE FROM Migrations WHERE Name = '@Name';"

        let migrationContent =
            sprintf "BEGIN TRANSACTION;\n%s\n%s\nEND TRANSACTION;" content insertStmn

        let queryParams =
            match migrationType with
            | MigrationType.Up ->
                dict
                    [ "Name" => migration.name
                      "Date" => DateTimeOffset.Now.ToUnixTimeMilliseconds() ]
            | MigrationType.Down -> dict [ "Name" => migration.name ]

        let query =
            querySingleOptionAsync (fun () -> connection) {
                script migrationContent
                parameters queryParams
            }

        Async.Catch query

    let private runMigrations (list: array<MigrationFile>) (migrationType: MigrationType) (connection: string) =
        match Array.isEmpty list with
        | false -> list |> Array.map (fun migrationFile -> vMigration migrationFile migrationType connection)
        | true -> Array.empty

    let private getPendingMigrations (lastMigration: Migration option) (migrations: MigrationFile array) =
        match lastMigration with
        | None -> migrations
        | Some migration ->
            let index = migrations |> Array.findIndex (fun m -> m.name = migration.Name)
            match index + 1 = migrations.Length with
            | true -> Array.empty
            | false ->
                let pending = migrations |> Array.skip (index + 1)
                pending

    let private getRanMigrations (lastMigration: Migration) (migrations: MigrationFile array) =
        let lastRanIndex = migrations |> Array.findIndex (fun m -> m.name = lastMigration.Name)
        migrations.[0..lastRanIndex]

    let private getLastMigration (conStr: string) =
        Async.Catch
            (querySeqAsync<Migration> (fun () -> getConnection conStr)
                 { script "Select * FROM Migrations ORDER BY Date DESC LIMIT 1;" })

    let runMigrationsNew (options: NewOptions) =
        let config = getSqlatorConfiguration()

        let path = Path.GetFullPath(config.migrationsDir)
        let timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
        let name = sprintf "%s_%i.sql" options.name timestamp

        let fullpath = Path.Combine(path, name)
        let filestr = File.Create(fullpath)

        let contentBytes =
            let content =
                sprintf "------------ SQLATOR:UP:%i --------------\n-- Write your Up migrations here\n\n" timestamp
                + (sprintf "------------ SQLATOR:DOWN:%i --------------\n-- Write how to revert the migration here"
                       timestamp)

            let bytes = Text.Encoding.UTF8.GetBytes(content)
            ReadOnlySpan<byte>(bytes)

        filestr.Write contentBytes
        filestr.Close()


    let runMigrationsUp (options: UpOptions) =
        let config = getSqlatorConfiguration()
        let path = Path.GetFullPath(config.migrationsDir)
        async {
            let! created = ensureMigrationsTableExists config.connection
            if not created then failwith "Could not create the Database/Migrations table"
            let! lastMigration = getLastMigration config.connection
            let migration =
                match lastMigration with
                | Choice1Of2 migration ->
                    printfn "Migrations found"
                    migration |> Seq.tryHead
                | Choice2Of2 ex ->
                    printfn "Migrations table not found, starting from scratch"
                    None

            let migrations = getMigrations path
            let pendingMigrations = getPendingMigrations migration migrations

            let migrationsToRun =
                match options.total |> Option.ofNullable with
                | Some total ->
                    let total =
                        match total >= pendingMigrations.Length with
                        | true -> pendingMigrations.Length - 1
                        | false -> total
                    match Array.isEmpty pendingMigrations with
                    | true -> Array.empty
                    | false -> pendingMigrations |> Array.take total
                | None -> pendingMigrations

            return! runMigrations migrationsToRun MigrationType.Up config.connection |> Async.Sequential
        }
        |> Async.RunSynchronously
        |> Array.map (fun query -> printfn "%A" query)
        |> ignore

    let runMigrationsDown (options: DownOptions) =
        let config = getSqlatorConfiguration()
        let path = Path.GetFullPath(config.migrationsDir)
        async {
            let! lastMigration = getLastMigration config.connection
            let migration =
                let message = "Database seems empty, try to run migrations first."
                match lastMigration with
                | Choice1Of2 migration ->
                    match migration |> Seq.tryHead with
                    | Some m -> m
                    | None -> failwith message
                | Choice2Of2 ex ->
                    printfn "%s" ex.Message
                    failwith message

            let migrations = getMigrations path
            let alreadyRanMigrations = getRanMigrations migration migrations |> Array.rev

            let amountToRunDown =
                match options.total |> Option.ofNullable with
                | Some number ->
                    match number > alreadyRanMigrations.Length with
                    | true ->
                        printfn "Total [%i] provided exceeds the amount of migrations in the database (%i)" number
                            alreadyRanMigrations.Length
                        alreadyRanMigrations.Length
                    | false -> number
                | None -> alreadyRanMigrations.Length

            return! runMigrations (alreadyRanMigrations |> Array.take amountToRunDown) MigrationType.Down
                        config.connection |> Async.Sequential
        }
        |> Async.RunSynchronously
        |> Array.map (fun query -> printfn "%A" query)
        |> ignore
