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
                    result {
                        let defaultPath =
                            opts.path
                            |> Option.ofObj
                            |> Option.defaultValue "./migrations"

                        let! config = tryGetOrCreateConfig "migrondi.json" defaultPath
                        return! tryGetOrCreateDirectory config.migrationsDir
                    }

                let message =
                    migrondiOutput {
                        normal $"Created:"
                        success "\"migrondi.json\""
                        normal " And "
                        success $"\"{migrationsDir}\"\n"
                    }
                    |> MigrondiOutput.toOutput opts.json

                MigrondiConsole.Log(message, opts.noColor |> not)
                return 0
            }

    let tryRunMigrationsNew: TryRunMigrationsNew =
        fun options config createMigration ->
            let logCreated (name: string) =
                let message =
                    migrondiOutput {
                        normal $"Created:"
                        success $"\"{name}\"\n"
                    }
                    |> MigrondiOutput.toOutput options.json


                MigrondiConsole.Log(message, options.noColor |> not)
                0

            let initMessage =
                migrondiOutput { normal "Running Migrondi New\n" }
                |> MigrondiOutput.toOutput options.json

            MigrondiConsole.Log(initMessage, options.noColor |> not)

            createMigration config.migrationsDir options.name
            |> Result.map logCreated

    let tryRunMigrationsUp: TryRunMigrationsUp =
        fun options config initializeDriver getConnection getPendingMigrations runDryMigrations runMigrations tryEnsureMigrationsTableExists tryGetLastMigrationPresent tryGetMigrationFiles ->

            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver

                MigrondiConsole.Log(
                    migrondiOutput { normal "Running Migrondi Up\n" }
                    |> MigrondiOutput.toOutput options.json,
                    options.noColor |> not
                )

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

                MigrondiConsole.Log(
                    migrondiOutput {
                        normal "Pending Migrations: "
                        warning $"{migrationsToRun.Length}\n"
                    }
                    |> MigrondiOutput.toOutput options.json,
                    options.noColor |> not
                )

                if options.dryRun then
                    MigrondiConsole.Log(
                        migrondiOutput { normal "Executing Dry Run:\n" }
                        |> MigrondiOutput.toOutput options.json,
                        options.noColor |> not
                    )

                    let migrations =
                        runDryMigrations driver MigrationType.Up migrationsToRun
                        |> Array.map
                            (fun (name, queryParams, content) ->
                                migrondiOutput {
                                    normal "MIGRATION: "
                                    warning $"{name} "
                                    normal "PARAMETERS: "
                                    warning (Spectre.Console.Markup.Escape($"%A{queryParams} "))
                                    normal $"CONTENT: \n{(Spectre.Console.Markup.Escape(content))}\n\n"
                                })
                        |> Array.fold
                            (fun (current: ConsoleOutput seq) next ->
                                seq {
                                    yield! next
                                    yield! current
                                })
                            Seq.empty
                        |> Seq.toList

                    MigrondiConsole.Log(migrations |> MigrondiOutput.toOutput options.json, options.noColor |> not)
                else
                    MigrondiConsole.Log(
                        migrondiOutput { normal "Executing Live Run:\n" }
                        |> MigrondiOutput.toOutput options.json,
                        options.noColor |> not
                    )

                    let amount =
                        runMigrations driver connection.Value MigrationType.Up migrationsToRun
                        |> Array.length

                    MigrondiConsole.Log(
                        migrondiOutput {
                            normal "Executed: "
                            warning $"{amount}"
                            normal "migrations\n"
                        }
                        |> MigrondiOutput.toOutput options.json,
                        options.noColor |> not
                    )

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

                MigrondiConsole.Log(
                    migrondiOutput { normal "Running Migrondi Down\n" }
                    |> MigrondiOutput.toOutput options.json,
                    options.noColor |> not
                )

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
                                    $"Total ({amount}) provided exceeds the amount of migrations in the database ({alreadyRanMigrations.Length})"

                                return! Error(AmountExeedsExistingException message)

                            return amount
                        else
                            return alreadyRanMigrations.Length
                    }

                let! amountToRunDown = getAmountToRunDown options.total

                MigrondiConsole.Log(
                    migrondiOutput {
                        normal "Rolling back: "
                        warning $"{amountToRunDown} "
                        normal "migrations\n"
                    }
                    |> MigrondiOutput.toOutput options.json,
                    options.noColor |> not
                )

                if options.dryRun then
                    MigrondiConsole.Log(
                        migrondiOutput { normal "Executing Dry Run:\n" }
                        |> MigrondiOutput.toOutput options.json,
                        options.noColor |> not
                    )

                    let migrations =
                        runDryMigrations driver MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)
                        |> Array.map
                            (fun (name, queryParams, content) ->
                                migrondiOutput {
                                    normal "MIGRATION: "
                                    warning $"{name} "
                                    normal "PARAMETERS: "
                                    warning (Spectre.Console.Markup.Escape($"%A{queryParams} "))
                                    normal $"CONTENT: \n{(Spectre.Console.Markup.Escape(content))}\n\n"
                                })
                        |> Array.fold
                            (fun (current: ConsoleOutput seq) next ->
                                seq {
                                    yield! next
                                    yield! current
                                })
                            Seq.empty
                        |> Seq.toList

                    MigrondiConsole.Log(migrations |> MigrondiOutput.toOutput options.json, options.noColor |> not)
                else
                    MigrondiConsole.Log(
                        migrondiOutput { normal "Executing Live Run:\n" }
                        |> MigrondiOutput.toOutput options.json,
                        options.noColor |> not
                    )

                    let amount =
                        runMigrations
                            driver
                            connection.Value
                            MigrationType.Down
                            (alreadyRanMigrations |> Array.take amountToRunDown)

                    MigrondiConsole.Log(
                        migrondiOutput {
                            normal "Executed: "
                            warning $"{amount}"
                            normal "migrations\n"
                        }
                        |> MigrondiOutput.toOutput options.json,
                        options.noColor |> not
                    )

                return 0
            }

    let tryListMigrations: TryListMigrations =
        fun options config initializeDriver getConnection getAppliedMigrations getPendingMigrations getMigrationName tryGetLastMigrationPresent tryGetMigrationFiles ->
            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver

                MigrondiConsole.Log(
                    migrondiOutput { normal "Running Migrondi List\n" }
                    |> MigrondiOutput.toOutput options.json,
                    options.noColor |> not
                )

                let! migration = tryGetLastMigrationPresent connection.Value

                let migrations =
                    tryGetMigrationFiles config.migrationsDir

                // TODO: Fix options behavior calls for #21

                return
                    match options.last, options.all, options.missing with
                    | (true, _, _) ->
                        match migration with
                        | Some migration ->
                            getMigrationName (MigrationSource.Database migration)
                            |> (fun name ->
                                MigrondiConsole.Log(
                                    migrondiOutput {
                                        normal "Last migration in the database is: "
                                        warning $"{name}\n"
                                    }
                                    |> MigrondiOutput.toOutput options.json,
                                    options.noColor |> not
                                ))

                        | None ->
                            MigrondiConsole.Log(
                                migrondiOutput { normal "No migrations have been run in the database\n" }
                                |> MigrondiOutput.toOutput options.json,
                                options.noColor |> not
                            )

                        0
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

                        MigrondiConsole.Log(
                            migrondiOutput {
                                normal "Missing migrations:\n"
                                warning $"{migrations}\n"
                            }
                            |> MigrondiOutput.toOutput options.json,
                            options.noColor |> not
                        )

                        0
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

                        MigrondiConsole.Log(
                            migrondiOutput {
                                normal "Present migrations in the database:\n"
                                warning $"{migrations}\n"
                            }
                            |> MigrondiOutput.toOutput options.json,
                            options.noColor |> not
                        )

                        0
                    | (_, _, _) ->
                        MigrondiConsole.Log(
                            migrondiOutput { danger "This flag combination is not supported" }
                            |> MigrondiOutput.toOutput options.json,
                            options.noColor |> not
                        )

                        let l1 = "--last true"
                        let l2 = "--all true --missing true"
                        let l3 = "--all true --missing false"

                        MigrondiConsole.Log(
                            migrondiOutput { normal $"Supported combinations are:\n{l1}\n{l2}\n{l3}\n" }
                            |> MigrondiOutput.toOutput options.json,
                            options.noColor |> not
                        )

                        1
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
