namespace Migrondi

open System
open System.Data
open Types
open Options
open Utils
open Queries

module Migrations =

    let runMigrationsNew (path: string, _: MigrondiConfig, _: Driver) (options: NewOptions) =
        let file, bytes = createNewMigrationFile path options.name

        file.Write(ReadOnlySpan<byte>(bytes))
        file.Close()

    let runMigrationsUp
        (connection: IDbConnection)
        (path: string, config: MigrondiConfig, driver: Driver)
        (options: UpOptions)
        =
        let created = ensureMigrationsTable driver connection
        if not created then raise (UnauthorizedAccessException "Could not create the Database/Migrations table")
        let migration = getLastMigration connection

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

        runMigrations driver connection MigrationType.Up migrationsToRun

    let runMigrationsDown
        (connection: IDbConnection)
        (path: string, config: MigrondiConfig, driver: Driver)
        (options: DownOptions)
        =
        let migration = getLastMigration connection

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

        runMigrations driver connection MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)

    let runMigrationsList
        (connection: IDbConnection)
        (path: string, _: MigrondiConfig, _: Driver)
        (options: ListOptions) =
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
        let migration = getLastMigration connection

        match last, all, missing with
        | (true, _, _) ->
            match migration with
            | Some migration ->
                printfn "Last migration in the database is %s" (migrationName (MigrationSource.Database migration))
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
