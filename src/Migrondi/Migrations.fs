namespace Migrondi

open System.Data
open Migrondi.Types
open Migrondi.FileSystem
open Migrondi.Writer
open Migrondi.Queries
open Migrondi.Options

open FsToolkit.ErrorHandling

module Migrations =

    let tryRunMigrationsInit
        (tryGetOrCreateDirectory: TryGetOrCreateDirectoryFn)
        (tryGetOrCreateConfig: TryGetOrCreateConfigFn)
        (opts: InitOptions)
        : Result<int, exn> =
        result {
            let! migrationsDir =
                tryGetOrCreateDirectory opts.path
                |> Result.map (tryGetOrCreateConfig "migrondi.json")
            // TODO: Print config and migrationsDir
            return 0
        }

    let tryRunMigrationsNew
        (createMigration: TryCreateFileFn)
        (config: MigrondiConfig)
        (options: NewOptions)
        : Result<int, exn> =
        let logCreated (name: string) =
            let createdMsg =
                [ ConsoleOutput.Success $"Created: {name}\n" ]


            if options.json then
                MigrondiConsole.Log(
                    JsonOutput.FromParts createdMsg
                    |> MigrondiOutput.JsonOutput
                )
            else
                MigrondiConsole.Log(createdMsg |> MigrondiOutput.ConsoleOutput, options.noColor |> not)

            0

        let initMessage =
            [ ConsoleOutput.Normal "Running Migrondi New\n" ]


        if options.json then
            MigrondiConsole.Log(
                JsonOutput.FromParts initMessage
                |> MigrondiOutput.JsonOutput
            )
        else
            MigrondiConsole.Log(initMessage |> MigrondiOutput.ConsoleOutput, options.noColor |> not)

        createMigration config.migrationsDir options.name
        |> Result.map logCreated

    let tryRunMigrationsUp
        (options: UpOptions)
        (config: MigrondiConfig)
        (getConnection: string -> Driver -> Lazy<IDbConnection>)
        (getMigrations: TryGetMigrationsFn)
        =
        result {
            let driver = Driver.FromString config.driver
            let connection = getConnection config.connection driver

            do! ensureMigrationsTable driver connection.Value

            let! migration = getLastMigration connection.Value
            let migrations = getMigrations config.migrationsDir

            let pendingMigrations =
                getPendingMigrations migration migrations

            let getAmount amountToRun =
                if amountToRun >= pendingMigrations.Length then
                    pendingMigrations.Length - 1
                else
                    amountToRun

            let migrationsToRun =
                if options.total > 0 then
                    let total = getAmount options.total

                    if Array.isEmpty pendingMigrations then
                        Array.empty
                    else
                        pendingMigrations.[..total]
                else
                    pendingMigrations

            if options.dryRun then
                let migrations =
                    dryRunMigrations driver MigrationType.Up migrationsToRun
                // TODO: Print migration content
                ()
            else
                let amount =
                    runMigrations driver connection.Value MigrationType.Up migrationsToRun
                    |> Array.length
                // TODO: Print success message
                ()

            return 0
        }

    let tryRunMigrationsDown
        (options: DownOptions)
        (config: MigrondiConfig)
        (getConnection: string -> Driver -> Lazy<IDbConnection>)
        (getMigrations: TryGetMigrationsFn)
        =
        result {
            let driver = Driver.FromString config.driver
            let connection = getConnection config.connection driver

            let! migration = getLastMigration connection.Value

            let migrations = getMigrations config.migrationsDir

            let alreadyRanMigrations =
                getAppliedMigrations migration migrations
                |> Array.rev

            let getAmountToRunDown amount =
                result {
                    if amount > 0 then
                        if amount > alreadyRanMigrations.Length then
                            let message =
                                $"Total [{amount}] provided exceeds the amount of migrations in the database ({alreadyRanMigrations.Length})"

                            return! Error(AmountExeedsExistingException message)

                        return amount
                    else
                        return alreadyRanMigrations.Length
                }

            let! amountToRunDown = getAmountToRunDown options.total

            if options.dryRun then
                let migrations =
                    dryRunMigrations driver MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)
                // TODO: Print migration content
                ()
            else
                let amount =
                    runMigrations
                        driver
                        connection.Value
                        MigrationType.Down
                        (alreadyRanMigrations |> Array.take amountToRunDown)
                // TODO: Print success message
                ()

            return 0
        }

    let tryRunMigrationsList
        (options: ListOptions)
        (config: MigrondiConfig)
        (getConnection: string -> Driver -> Lazy<IDbConnection>)
        (getMigrations: TryGetMigrationsFn)
        =
        result {
            let driver = Driver.FromString config.driver
            let connection = getConnection config.connection driver

            let! migration = getLastMigration connection.Value

            let migrations = getMigrations config.migrationsDir

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
                    |> fun arr -> System.String.Join("", arr)

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
                    |> fun arr -> System.String.Join("", arr)

                printfn $"Migrations that have been ran:\n{migrations}"
            | (_, _, _) ->
                printfn "This flag combination is not supported"
                let l1 = "--last true"
                let l2 = "--all true --missing true"
                let l3 = "--all true --missing false"
                printfn $"Suported comibations are:\n{l1}\n{l2}\n{l3}"

            return 0
        }
