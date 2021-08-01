namespace Migrondi

open System
open System.Data
open Types
open Options
open Utils
open Queries
open UserInterface

module Migrations =

    let runMigrationsInit (opts: InitOptions) =
        let path, migrationsPath = getInitPathAndMigrationsPath opts.path

        match checkExistsPathAndMigrationsDir path migrationsPath with
        | (true, _) -> raise (ArgumentException """"migrondi.json" already exists, aborting.""")
        | _ ->
            createMigrationsDir migrationsPath |> ignore

            let file, content =
                createMigrondiConfJson path migrationsPath

            file.Write(ReadOnlySpan<byte>(content))
            successPrint $"Created %s{file.Name} and %s{migrationsPath}"
            file.Dispose()



    let runMigrationsNew (path: string, _: MigrondiConfig, _: Driver) (options: NewOptions) =
        let file, bytes =
            createNewMigrationFile path options.name

        file.Write(ReadOnlySpan<byte>(bytes))
        successPrint $"""Created: {file.Name}"""
        file.Dispose()

    let runMigrationsUp (connection: IDbConnection)
                        (path: string, config: MigrondiConfig, driver: Driver)
                        (options: UpOptions)
                        =
        let created = ensureMigrationsTable driver connection

        if not created
        then raise (UnauthorizedAccessException "Could not create the Database/Migrations table")

        let migration = getLastMigration connection

        let migrations = getMigrations path

        let pendingMigrations =
            getPendingMigrations migration migrations

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

        match options.dryRun |> Option.ofNullable with
        | Some true -> dryRunMigrations driver MigrationType.Up migrationsToRun
        | Some false
        | _ -> runMigrations driver connection MigrationType.Up migrationsToRun

    let runMigrationsDown (connection: IDbConnection)
                          (path: string, _: MigrondiConfig, driver: Driver)
                          (options: DownOptions)
                          =
        let migration = getLastMigration connection

        let migrations = getMigrations path

        let alreadyRanMigrations =
            getAppliedMigrations migration migrations
            |> Array.rev

        let amountToRunDown =
            match options.total |> Option.ofNullable with
            | Some number ->
                match number > alreadyRanMigrations.Length with
                | true ->
                    successPrint
                        $"Total [{number}] provided exceeds the amount of migrations in the database ({alreadyRanMigrations.Length})"

                    alreadyRanMigrations.Length
                | false -> number
            | None -> alreadyRanMigrations.Length

        match options.dryRun |> Option.ofNullable with
        | Some true -> dryRunMigrations driver MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)
        | Some false
        | _ -> runMigrations driver connection MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)

    let runMigrationsList (connection: IDbConnection)
                          (path: string, _: MigrondiConfig, _: Driver)
                          (options: ListOptions)
                          =
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
                migrationName (MigrationSource.Database migration)
                |> (fun name -> successPrint $"Last migration in the database is {name}")
                
            | None -> successPrint "No migrations have been run in the database"
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

            successPrint $"Missing migrations:\n{migrations}"
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

            successPrint $"Migrations that have been ran:\n{migrations}"
        | (_, _, _) ->
            successPrint "This flag combination is not supported"
            let l1 = "--last true"
            let l2 = "--all true --missing true"
            let l3 = "--all true --missing false"
            successPrint $"Suported comibations are:\n{l1}\n{l2}\n{l3}"
