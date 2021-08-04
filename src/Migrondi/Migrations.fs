namespace Migrondi

open System
open System.Data
open Migrondi.Types
open Options
open Utils
open Queries
open FsToolkit.ErrorHandling
open System.IO
open System.Text.Json

module Migrations =

    let runMigrationsInit (opts: InitOptions) : Result<string * string, exn> =
        result {
            let! dir = getOrCreateMigrationsDir opts.path
            let migrationsPath = Path.Combine(dir.FullName, "migrations")

            use! file =
                try
                    Ok
                    <| File.Create(Path.Combine(opts.path, "migrondi.json"))
                with
                | ex -> Error ex

            let content =
                let opts = JsonSerializerOptions()
                opts.WriteIndented <- true
                opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase

                JsonSerializer.SerializeToUtf8Bytes(
                    { connection = "Data Source=migrondi.db"
                      migrationsDir = migrationsPath
                      driver = "sqlite" },
                    opts
                )

            try
                file.Write(ReadOnlySpan<byte>(content))
            with
            | ex -> do! Error ex

            return (file.Name, migrationsPath)
        }



    let runMigrationsNew (path: string, _: MigrondiConfig, _: Driver) (options: NewOptions) =
        let file, bytes =
            createNewMigrationFile path options.name

        file.Write(ReadOnlySpan<byte>(bytes))
        printfn $"""Created: {file.Name}"""
        file.Dispose()

    let runMigrationsUp
        (connection: IDbConnection)
        (path: string, config: MigrondiConfig, driver: Driver)
        (options: UpOptions)
        =
        let created = ensureMigrationsTable driver connection

        if not created then
            raise (UnauthorizedAccessException "Could not create the Database/Migrations table")

        let migration = getLastMigration connection

        let migrations = getMigrations path

        let pendingMigrations =
            getPendingMigrations migration migrations

        let migrationsToRun =
            match options.total with
            | total when total > 0 ->
                let total =
                    match total >= pendingMigrations.Length with
                    | true -> pendingMigrations.Length - 1
                    | false -> total

                match Array.isEmpty pendingMigrations with
                | true -> Array.empty
                | false ->
                    let amount =
                        if total >= pendingMigrations.Length then
                            pendingMigrations.Length - 1
                        else
                            total

                    pendingMigrations.[..amount]
            | _ -> pendingMigrations

        match options.dryRun with
        | true -> dryRunMigrations driver MigrationType.Up migrationsToRun
        | false -> runMigrations driver connection MigrationType.Up migrationsToRun

    let runMigrationsDown
        (connection: IDbConnection)
        (path: string, _: MigrondiConfig, driver: Driver)
        (options: DownOptions)
        =
        let migration = getLastMigration connection

        let migrations = getMigrations path

        let alreadyRanMigrations =
            getAppliedMigrations migration migrations
            |> Array.rev

        let amountToRunDown =
            match options.total with
            | number when number > 0 ->
                match number > alreadyRanMigrations.Length with
                | true ->
                    printfn
                        $"Total [{number}] provided exceeds the amount of migrations in the database ({alreadyRanMigrations.Length})"

                    alreadyRanMigrations.Length
                | false -> number
            | _ -> alreadyRanMigrations.Length

        match options.dryRun with
        | true -> dryRunMigrations driver MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)
        | false ->
            runMigrations driver connection MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)

    let runMigrationsList
        (connection: IDbConnection)
        (path: string, _: MigrondiConfig, _: Driver)
        (options: ListOptions)
        =

        let migrations = getMigrations path
        let migration = getLastMigration connection

        match options.last, options.all, options.missing with
        | (true, _, _) ->
            match migration with
            | Some migration ->
                migrationName (MigrationSource.Database migration)
                |> (fun name -> printfn $"Last migration in the database is {name}")

            | None -> printfn "No migrations have been run in the database"
        | (_, true, true) ->
            let pendingMigrations =
                getPendingMigrations migration migrations

            let migrations =
                pendingMigrations
                |> Array.map (
                    MigrationSource.File
                    >> migrationName
                    >> sprintf "%s\n"
                )
                |> fun arr -> String.Join("", arr)

            printfn $"Missing migrations:\n{migrations}"
        | (_, true, false) ->
            let alreadyRan =
                getAppliedMigrations migration migrations

            let migrations =
                alreadyRan
                |> Array.map (
                    MigrationSource.File
                    >> migrationName
                    >> sprintf "%s\n"
                )
                |> fun arr -> String.Join("", arr)

            printfn $"Migrations that have been ran:\n{migrations}"
        | (_, _, _) ->
            printfn "This flag combination is not supported"
            let l1 = "--last true"
            let l2 = "--all true --missing true"
            let l3 = "--all true --missing false"
            printfn $"Suported comibations are:\n{l1}\n{l2}\n{l3}"
