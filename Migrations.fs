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
            querySingleAsync<int> (fun () -> connection) {
                script """
            CREATE TABLE IF NOT EXISTS Migrations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(255) NOT NULL,
                Date INTEGER NOT NULL
            );
            """
            }
        Async.Catch query

    let runNewMigration (options: NewOptions) =
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


    let ensureMigrationsTableExists (constr: string) =
        let connection = getConnection constr
        async {
            let! migrationsTableExists = doesMigrationsTableExists connection
            let tableExistsOrCreated =
                let connection = getConnection constr
                async {
                    match migrationsTableExists with
                    | false ->
                        let! result = createMigrationsTable connection
                        return match result with
                               | Choice1Of2 result -> true
                               | Choice2Of2 exn ->
                                   printfn "%s" exn.Message
                                   false
                    | true -> return true
                }
            return! tableExistsOrCreated
        }

    let runUpMigration (options: UpOptions) =
        let config = getSqlatorConfiguration()
        let path = Path.GetFullPath(config.migrationsDir)
        let migrations = getMigrations path
        async {
            let! created = ensureMigrationsTableExists config.connection
            printfn "%A" created } |> Async.RunSynchronously
