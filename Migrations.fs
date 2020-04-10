namespace Sqlator

open System
open FSharp.Data.Dapper
open Types
open Options
open Utils
open Utils.Operators

module Migrations =

    let private asyncDoesMigrationsTableExists (connection: Connection) =
        async {
            let query =
                querySeqAsync<Migration> (fun () -> connection)
                    { script "Select * FROM Migrations ORDER BY Timestamp DESC LIMIT 1;" }
            let! getMigrationRecords = Async.Catch query
            return match getMigrationRecords with
                   | Choice1Of2 result -> true
                   | Choice2Of2 exn ->
                       printfn "%s" exn.Message
                       false
        }

    let private asyncCreateMigrationsTable (connection: Connection) =
        let query =
            querySingleOptionAsync (fun () -> connection) {
                script """
            CREATE TABLE Migrations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(255) NOT NULL,
                Timestamp INTEGER NOT NULL);
            """
            }
        Async.Catch query

    let private asyncEnsureMigrationsTableExists (driver: Driver) (constr: string) =
        async {
            let connection = getConnection driver constr
            let! tableExists = asyncDoesMigrationsTableExists connection
            match tableExists with
            | false ->
                let connection = getConnection driver constr
                let! result = asyncCreateMigrationsTable connection
                return match result with
                       | Choice1Of2 _ -> true
                       | Choice2Of2 exn ->
                           printfn "%s" exn.Message
                           false
            | true -> return true
        }

    let private asyncApplyMigration
        (driver: Driver)
        (connection: string)
        (migrationType: MigrationType)
        (migration: MigrationFile)
        =
        let content =
            match migrationType with
            | MigrationType.Up -> migration.upContent
            | MigrationType.Down -> migration.downContent

        let insertStmn =
            match migrationType with
            | MigrationType.Up -> "INSERT INTO Migrations (Name, Timestamp) VALUES(@Name, @Timestamp);"
            | MigrationType.Down -> "DELETE FROM Migrations WHERE Timestamp = @Timestamp;"

        let migrationContent =
            sprintf "BEGIN TRANSACTION;\n%s\n%s\nEND TRANSACTION;" content insertStmn

        let queryParams =
            match migrationType with
            | MigrationType.Up ->
                dict
                    [ "Name" => migration.name
                      "Timestamp" => migration.timestamp ]
            | MigrationType.Down -> dict [ "Timestamp" => migration.timestamp ]

        querySingleOptionAsync (fun () -> getConnection driver connection) {
            script migrationContent
            parameters queryParams
        }

    let private asyncRunMigrations
        (driver: Driver)
        (connection: string)
        (migrationType: MigrationType)
        (migrationFiles: array<MigrationFile>)
        =
        let asyncApplyMigrationWithConnectionAndType = asyncApplyMigration driver connection migrationType
        match Array.isEmpty migrationFiles with
        | false -> migrationFiles |> Array.map asyncApplyMigrationWithConnectionAndType
        | true -> Array.empty

    let private asyncGetLastMigration (driver: Driver) (connection: string) =
        Async.Catch
            (querySeqAsync<Migration> (fun () -> getConnection driver connection)
                 { script "Select * FROM Migrations ORDER BY Timestamp DESC LIMIT 1;" })

    let runMigrationsNew (options: NewOptions) =
        let (path, _) = getPathAndConfig()

        let file, bytes = createNewMigrationFile path options.name

        file.Write(ReadOnlySpan<byte>(bytes))
        file.Close()

    let asyncRunMigrationsUp (options: UpOptions) =
        let operation =
            async {
                let (path, config) = getPathAndConfig()
                let driver = Driver.FromString config.driver
                let! created = asyncEnsureMigrationsTableExists driver config.connection
                if not created then raise (UnauthorizedAccessException "Could not create the Database/Migrations table")
                let! lastMigration = asyncGetLastMigration driver config.connection
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

                return! asyncRunMigrations driver config.connection MigrationType.Up migrationsToRun |> Async.Sequential
            }
        Async.Catch operation

    let runMigrationsDown (options: DownOptions) =
        let operation =
            async {
                let (path, config) = getPathAndConfig()
                let driver = Driver.FromString config.driver
                let! lastMigration = asyncGetLastMigration driver config.connection
                let migration =
                    match lastMigration with
                    | Choice1Of2 migration -> migration |> Seq.tryHead
                    | Choice2Of2 ex ->
                        printfn "%s" ex.Message
                        raise (InvalidOperationException "Database seems empty, try to run migrations first.")

                let migrations = getMigrations path
                let alreadyRanMigrations = getAppliedMigrations migration migrations |> Array.rev

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

                return! asyncRunMigrations driver config.connection MigrationType.Down
                            (alreadyRanMigrations |> Array.take amountToRunDown) |> Async.Sequential
            }
        Async.Catch operation

    let runMigrationsList (options: ListOptions) =
        let operation =
            async {
                let (path, config) = getPathAndConfig()

                let all =
                    match options.all |> Option.ofNullable with
                    | Some total -> total
                    | None -> false

                let missing =
                    match options.missing |> Option.ofNullable with
                    | Some missing -> missing
                    | None -> false

                let last =
                    match options.last |> Option.ofNullable with
                    | Some last -> last
                    | None -> false

                let migrations = getMigrations path
                let! lastMigration = asyncGetLastMigration (Driver.FromString config.driver) config.connection
                let migration =
                    match lastMigration with
                    | Choice1Of2 migration -> migration |> Seq.tryHead
                    | Choice2Of2 ex -> None

                match last, all, missing with
                | (true, _, _) ->
                    match migration with
                    | Some migration ->
                        printfn "Last migration in the database is %s"
                            (migrationName (MigrationSource.Database migration))
                    | None -> printfn "No migrations have been run in the database"
                | (_, true, true) ->
                    let pendingMigrations = getPendingMigrations migration migrations

                    let migrations =
                        pendingMigrations
                        |> Array.map
                            (MigrationSource.File
                             >> migrationName
                             >> sprintf "%s\n")
                        |> fun arr -> String.Join("", arr)
                    printfn "Missing migrations:\n%s" migrations
                | (_, true, false) ->
                    let alreadyRan = getAppliedMigrations migration migrations

                    let migrations =
                        alreadyRan
                        |> Array.map
                            (MigrationSource.File
                             >> migrationName
                             >> sprintf "%s\n")
                        |> fun arr -> String.Join("", arr)

                    printfn "Migrations that have been ran:\n%s" migrations
                | (_, _, _) ->
                    printfn "This flag combination is not supported"
                    let combinations = """
                        --last true
                        --all true --missing true
                        --all true --missing false
                        """
                    printfn "Suported comibations are: %s" combinations
            }

        Async.Catch operation
