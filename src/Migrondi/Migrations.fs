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

type TryGetFileStatus =
    StatusOptions -> MigrondiConfig -> InitializeDriver -> GetConnection -> TryGetByFilename -> Result<int, exn>

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
                        successln $"\"{migrationsDir}\""
                    }

                MigrondiConsole.Log(message, opts.noColor |> not, opts.json)
                return 0
            }

    let tryRunMigrationsNew: TryRunMigrationsNew =
        fun options config createMigration ->
            let logCreated (name: string) =
                let message =
                    migrondiOutput {
                        normal $"Created:"
                        successln $"\"{name}\""
                    }


                MigrondiConsole.Log(message, options.noColor |> not)
                0

            let initMessage =
                migrondiOutput { normalln "Running Migrondi New" }

            MigrondiConsole.Log(initMessage, options.noColor |> not, options.json)

            createMigration config.migrationsDir options.name
            |> Result.map logCreated

    let tryRunMigrationsUp: TryRunMigrationsUp =
        fun options config initializeDriver getConnection getPendingMigrations runDryMigrations runMigrations tryEnsureMigrationsTableExists tryGetLastMigrationPresent tryGetMigrationFiles ->

            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver

                MigrondiConsole.Log(
                    migrondiOutput { normalln "Running Migrondi Up" },
                    options.noColor |> not,
                    options.json
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
                        warningln $"{migrationsToRun.Length}"
                    },
                    options.noColor |> not,
                    options.json
                )

                if options.dryRun then
                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Executing Dry Run:" },
                        options.noColor |> not,
                        options.json
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
                                    normalln $"CONTENT: \n{(Spectre.Console.Markup.Escape(content))}"
                                })
                        |> Array.fold
                            (fun (current: ConsoleOutput seq) next ->
                                seq {
                                    yield! next
                                    yield! current
                                })
                            Seq.empty
                        |> Seq.toList

                    MigrondiConsole.Log(migrations, options.noColor |> not, options.json)
                else
                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Executing Live Run:" },
                        options.noColor |> not,
                        options.json
                    )

                    let amount =
                        runMigrations driver connection.Value MigrationType.Up migrationsToRun
                        |> Array.length

                    MigrondiConsole.Log(
                        migrondiOutput {
                            normal "Executed: "
                            warning $"%i{amount} "
                            normalln "migrations"
                        },
                        options.noColor |> not,
                        options.json
                    )

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
                    migrondiOutput { normalln "Running Migrondi Down" },
                    options.noColor |> not,
                    options.json
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
                        warning $"%i{amountToRunDown} "
                        normalln "migrations"
                    },
                    options.noColor |> not,
                    options.json
                )

                if options.dryRun then
                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Executing Dry Run:" },
                        options.noColor |> not,
                        options.json
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
                                    normalln $"CONTENT: \n{(Spectre.Console.Markup.Escape(content))}\n"
                                })
                        |> Array.fold
                            (fun (current: ConsoleOutput seq) next ->
                                seq {
                                    yield! next
                                    yield! current
                                })
                            Seq.empty
                        |> Seq.toList

                    MigrondiConsole.Log(migrations, options.noColor |> not, options.json)
                else
                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Executing Live Run:" },
                        options.noColor |> not,
                        options.json
                    )

                    let amount =
                        runMigrations
                            driver
                            connection.Value
                            MigrationType.Down
                            (alreadyRanMigrations |> Array.take amountToRunDown)
                        |> Array.length

                    MigrondiConsole.Log(
                        migrondiOutput {
                            normal "Executed: "
                            warning $"%i{amount} "
                            normalln "migrations"
                        },
                        options.noColor |> not,
                        options.json
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
                    migrondiOutput { normalln "Running Migrondi List" },
                    options.noColor |> not,
                    options.json
                )

                let! migration = tryGetLastMigrationPresent connection.Value

                let migrations =
                    tryGetMigrationFiles config.migrationsDir

                let getPendingMigrations () =
                    getPendingMigrations migration migrations

                let getPresentMigrations () =
                    getAppliedMigrations migration migrations

                let getMigrationLog (migrations: MigrationFile array) asPresent =
                    migrations
                    |> Array.map
                        (fun m ->
                            let isPresent = if asPresent then "x" else ""

                            migrondiOutput {
                                normal (Spectre.Console.Markup.Escape($"- [ {isPresent} ] "))
                                warningln $"{m.name} - {m.timestamp}"
                            })
                    |> Array.fold
                        (fun (current: ConsoleOutput seq) next ->
                            seq {
                                yield! next
                                yield! current
                            })
                        Seq.empty
                    |> Seq.toList

                match options.listKind with
                | MigrationListEnum.Pending ->
                    let migrations = getPendingMigrations ()
                    let log = getMigrationLog migrations false

                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Pending Migrations:" },
                        options.noColor |> not,
                        options.json
                    )

                    MigrondiConsole.Log(log, options.noColor |> not, options.json)
                    return 0
                | MigrationListEnum.Present ->
                    let migrations = getPresentMigrations ()
                    let log = getMigrationLog migrations true

                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Present Migrations:" },
                        options.noColor |> not,
                        options.json
                    )

                    MigrondiConsole.Log(log, options.noColor |> not, options.json)
                    return 0
                | MigrationListEnum.Both ->
                    let pending = getPendingMigrations ()
                    let present = getPresentMigrations ()

                    let logPresent = getMigrationLog present true
                    let logPending = getMigrationLog pending false

                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Present Migrations:" },
                        options.noColor |> not,
                        options.json
                    )

                    MigrondiConsole.Log(logPresent, options.noColor |> not, options.json)

                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Pending Migrations:" },
                        options.noColor |> not,
                        options.json
                    )

                    MigrondiConsole.Log(logPending, options.noColor |> not, options.json)
                    return 0
                | _ -> return 1

            }

    let truRunMigrationsStatus: TryGetFileStatus =
        fun options config initializeDriver getConnection tryGetFileByName ->
            result {
                let driver = Driver.FromString config.driver
                do initializeDriver driver
                let connection = getConnection config.connection driver

                MigrondiConsole.Log(
                    migrondiOutput { normalln "Running Migrondi Status" },
                    options.noColor |> not,
                    options.json
                )


                let! exists = tryGetFileByName connection.Value options.filename

                let log =
                    let output = 
                        if exists then
                            migrondiOutput { successln $"Present" }
                        else
                           migrondiOutput { warningln $"Pending" }
                    output
                    |> List.append (migrondiOutput { normal $"Filename: {options.filename} is " })

                MigrondiConsole.Log(log, options.noColor |> not, options.json)
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

    static member RunStatus(options: StatusOptions, ?config: MigrondiConfig) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! FileSystem.TryGetMigrondiConfig()
                }

            return!
                Migrations.truRunMigrationsStatus
                    options
                    config
                    Queries.initializeDriver
                    Queries.getConnection
                    Queries.tryGetByFilename
        }
