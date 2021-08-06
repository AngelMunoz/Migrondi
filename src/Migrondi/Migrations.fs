namespace Migrondi.Migrations

open Migrondi.Types
open Migrondi.FileSystem
open Migrondi.Writer
open Migrondi.Database
open Migrondi.Options

open FsToolkit.ErrorHandling

type TryRunInit = InitOptions -> TryGetOrCreateDirectoryFn -> TryGetOrCreateConfigFn -> Result<int, exn>

type TryRunMigrationsNew = NewOptions -> MigrondiConfig -> TryCreateFileFn -> Result<int, exn>

type TryRunMigrationsUp =
    UpOptions
        -> MigrondiConfig
        -> InitializeDriver
        -> GetConnection
        -> GetMigrations
        -> RunDryMigrations
        -> RunMigrations
        -> TryEnsureMigrationsTableExists
        -> TryGetLastMigrationInDatabase
        -> TryGetMigrationsFn
        -> Result<int, exn>

type TryRunMigrationsDown =
    DownOptions
        -> MigrondiConfig
        -> InitializeDriver
        -> GetConnection
        -> GetMigrations
        -> RunDryMigrations
        -> RunMigrations
        -> TryEnsureMigrationsTableExists
        -> TryGetLastMigrationInDatabase
        -> TryGetMigrationsFn
        -> Result<int, exn>

type TryListMigrations =
    ListOptions
        -> MigrondiConfig
        -> InitializeDriver
        -> GetConnection
        -> GetMigrations
        -> GetMigrations
        -> GetMigrationName
        -> TryGetLastMigrationInDatabase
        -> TryGetMigrationsFn
        -> Result<int, exn>


[<RequireQualifiedAccess>]
module Migrations =

    let tryRunMigrationsInit: TryRunInit =
        fun opts tryGetOrCreateDirectory tryGetOrCreateConfig ->
            result {
                let! migrationsDir =
                    tryGetOrCreateDirectory opts.path
                    |> Result.map (tryGetOrCreateConfig "migrondi.json")
                // TODO: Print config and migrationsDir
                return 0
            }

    let tryRunMigrationsNew: TryRunMigrationsNew =
        fun options config createMigration ->
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

    let tryRunMigrationsUp: TryRunMigrationsUp =
        fun options config initializeDriver getConnection getPendingMigrations runDryMigrations runMigrations tryEnsureMigrationsTableExists tryGetLastMigrationPresent tryGetMigrationFiles ->

            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver


                do! tryEnsureMigrationsTableExists driver connection.Value

                let! migration = tryGetLastMigrationPresent connection.Value

                let migrations =
                    tryGetMigrationFiles config.migrationsDir

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
                        runDryMigrations driver MigrationType.Up migrationsToRun
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

    let tryRunMigrationsDown: TryRunMigrationsDown =
        fun options config initializeDriver getConnection getAppliedMigrations runDryMigrations runMigrations tryEnsureMigrationsTableExists tryGetLastMigrationPresent tryGetMigrationFiles ->

            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver

                do! tryEnsureMigrationsTableExists driver connection.Value

                let! migration = tryGetLastMigrationPresent connection.Value

                let migrations =
                    tryGetMigrationFiles config.migrationsDir

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
                        runDryMigrations driver MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)
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

    let tryListMigrations: TryListMigrations =
        fun options config initializeDriver getConnection getAppliedMigrations getPendingMigrations getMigrationName tryGetLastMigrationPresent tryGetMigrationFiles ->
            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver

                let! migration = tryGetLastMigrationPresent connection.Value

                let migrations =
                    tryGetMigrationFiles config.migrationsDir

                // TODO: Fix these console calls

                match options.last, options.all, options.missing with
                | (true, _, _) ->
                    match migration with
                    | Some migration ->
                        getMigrationName (MigrationSource.Database migration)
                        |> (fun name -> printfn $"Last migration in the database is {name}")

                    | None -> printfn "No migrations have been run in the database"
                | (_, true, true) ->
                    let pendingMigrations =
                        getPendingMigrations migration migrations

                    let migrations =
                        pendingMigrations
                        |> Array.map (
                            MigrationSource.File
                            >> getMigrationName
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
                            >> getMigrationName
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

type MigrondiRunner() =

    static member RunInit(options: InitOptions) : Result<int, exn> =
        Migrations.tryRunMigrationsInit
            options
            FileSystem.TryGetOrCreateDirectory
            FileSystem.TryGetOrCreateConfiguration

    static member RunNew(options: NewOptions, ?config: MigrondiConfig) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! FileSystem.TryGetMigrondiConfig()
                }

            return! Migrations.tryRunMigrationsNew options config FileSystem.TryCreateNewMigrationFile
        }

    static member RunUp(options: UpOptions, ?config: MigrondiConfig) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! FileSystem.TryGetMigrondiConfig()
                }

            return!
                Migrations.tryRunMigrationsUp
                    options
                    config
                    Queries.initializeDriver
                    Queries.getConnection
                    Queries.getPendingMigrations
                    Queries.dryRunMigrations
                    Queries.runMigrations
                    Queries.ensureMigrationsTable
                    Queries.getLastMigration
                    FileSystem.GetMigrations
        }

    static member RunDown(options: DownOptions, ?config: MigrondiConfig) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! FileSystem.TryGetMigrondiConfig()
                }

            return!
                Migrations.tryRunMigrationsDown
                    options
                    config
                    Queries.initializeDriver
                    Queries.getConnection
                    Queries.getAppliedMigrations
                    Queries.dryRunMigrations
                    Queries.runMigrations
                    Queries.ensureMigrationsTable
                    Queries.getLastMigration
                    FileSystem.GetMigrations
        }

    static member RunList(options: ListOptions, ?config: MigrondiConfig) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! FileSystem.TryGetMigrondiConfig()
                }

            return!
                Migrations.tryListMigrations
                    options
                    config
                    Queries.initializeDriver
                    Queries.getConnection
                    Queries.getAppliedMigrations
                    Queries.getPendingMigrations
                    Queries.getMigrationName
                    Queries.getLastMigration
                    FileSystem.GetMigrations
        }
