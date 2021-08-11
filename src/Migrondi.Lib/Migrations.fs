namespace Migrondi.Migrations

open System
open System.Runtime.InteropServices
open System.Collections.Generic
open System.Data
open Migrondi.Types
open Migrondi.FileSystem
open Migrondi.Writer
open Migrondi.Database

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
        -> TryGetLastMigrationInDatabase
        -> TryGetMigrationsFn
        -> Result<int, exn>

type TryGetFileStatus =
    StatusOptions -> MigrondiConfig -> InitializeDriver -> GetConnection -> TryGetByFilename -> Result<int, exn>

[<RequireQualifiedAccess>]
module Migrations =

    let private getMigrationLogs migrations =
        seq {
            for (name, queryParams, content) in migrations do
                yield!
                    (migrondiOutput {
                        normal "MIGRATION: "
                        warning $"{name} "
                        normal "PARAMETERS: "
                        warning $"%A{queryParams}"
                        normalln $"CONTENT: \n{content}"
                     })
        }
        |> Seq.toList

    let tryRunMigrationsInit: TryRunInit =
        fun options tryGetOrCreateDirectory tryGetOrCreateConfig ->
            result {
                let! migrationsDir =
                    result {

                        let! config = tryGetOrCreateConfig "migrondi.json" options.path
                        return! tryGetOrCreateDirectory config.migrationsDir
                    }

                let message =
                    migrondiOutput {
                        normal $"Created:"
                        success "\"migrondi.json\""
                        normal " And "
                        successln $"\"{migrationsDir}\""
                    }

                MigrondiConsole.Log(message, options.noColor, options.json)
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


                MigrondiConsole.Log(message, options.noColor)
                0

            let initMessage =
                migrondiOutput { normalln "Running Migrondi New" }

            MigrondiConsole.Log(initMessage, options.noColor, options.json)

            createMigration config.migrationsDir options.name
            |> Result.map logCreated

    let tryRunMigrationsUp: TryRunMigrationsUp =
        fun options config initializeDriver getConnection getPendingMigrations runDryMigrations runMigrations tryEnsureMigrationsTableExists tryGetLastMigrationPresent tryGetMigrationFiles ->

            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver

                MigrondiConsole.Log(migrondiOutput { normalln "Running Migrondi Up" }, options.noColor, options.json)

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
                    options.noColor,
                    options.json
                )

                if options.dryRun then
                    MigrondiConsole.Log(migrondiOutput { normalln "Executing Dry Run:" }, options.noColor, options.json)

                    let migrations =
                        runDryMigrations driver MigrationType.Up migrationsToRun
                        |> getMigrationLogs

                    MigrondiConsole.Log(migrations, options.noColor, options.json)
                else
                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Executing Live Run:" },
                        options.noColor,
                        options.json
                    )

                    let! amount = runMigrations driver connection.Value MigrationType.Up migrationsToRun

                    MigrondiConsole.Log(
                        migrondiOutput {
                            normal "Executed: "
                            warning $"%i{amount.Length} "
                            normalln "migrations"
                        },
                        options.noColor,
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

                MigrondiConsole.Log(migrondiOutput { normalln "Running Migrondi Down" }, options.noColor, options.json)

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
                    options.noColor,
                    options.json
                )

                if options.dryRun then
                    MigrondiConsole.Log(migrondiOutput { normalln "Executing Dry Run:" }, options.noColor, options.json)

                    let migrations =
                        runDryMigrations driver MigrationType.Down (alreadyRanMigrations |> Array.take amountToRunDown)
                        |> getMigrationLogs

                    MigrondiConsole.Log(migrations, options.noColor, options.json)
                else
                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Executing Live Run:" },
                        options.noColor,
                        options.json
                    )

                    let! amount =
                        runMigrations
                            driver
                            connection.Value
                            MigrationType.Down
                            (alreadyRanMigrations |> Array.take amountToRunDown)

                    MigrondiConsole.Log(
                        migrondiOutput {
                            normal "Executed: "
                            warning $"%i{amount.Length} "
                            normalln "migrations"
                        },
                        options.noColor,
                        options.json
                    )

                return 0
            }

    let tryListMigrations: TryListMigrations =
        fun options config initializeDriver getConnection getAppliedMigrations getPendingMigrations tryGetLastMigrationPresent tryGetMigrationFiles ->
            result {
                let driver = Driver.FromString config.driver

                do initializeDriver driver
                let connection = getConnection config.connection driver

                MigrondiConsole.Log(migrondiOutput { normalln "Running Migrondi List" }, options.noColor, options.json)

                let! migration = tryGetLastMigrationPresent connection.Value

                let migrations =
                    tryGetMigrationFiles config.migrationsDir

                let getPendingMigrations () =
                    getPendingMigrations migration migrations

                let getPresentMigrations () =
                    getAppliedMigrations migration migrations

                let getMigrationLog (migrations: MigrationFile array) asPresent =
                    seq {
                        for row in migrations do
                            let isPresent = if asPresent then "x" else ""

                            yield!
                                (migrondiOutput {
                                    normal ($"- [ {isPresent} ] ")
                                    warningln $"{row.name} - {row.timestamp}"
                                 })
                    }
                    |> Seq.toList

                match options.listKind with
                | MigrationListEnum.Pending ->
                    let migrations = getPendingMigrations ()
                    let log = getMigrationLog migrations false

                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Pending Migrations:" },
                        options.noColor,
                        options.json
                    )

                    MigrondiConsole.Log(log, options.noColor, options.json)
                    return 0
                | MigrationListEnum.Present ->
                    let migrations = getPresentMigrations ()
                    let log = getMigrationLog migrations true

                    MigrondiConsole.Log(
                        migrondiOutput { normalln "Present Migrations:" },
                        options.noColor,
                        options.json
                    )

                    MigrondiConsole.Log(log, options.noColor, options.json)
                    return 0
                | MigrationListEnum.Both ->
                    let pending = getPendingMigrations ()
                    let present = getPresentMigrations ()

                    let log =
                        [ yield! getMigrationLog present true
                          yield! getMigrationLog pending false ]

                    MigrondiConsole.Log(log, options.noColor, options.json)

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
                    options.noColor,
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

                MigrondiConsole.Log(log, options.noColor, options.json)
                return 0
            }

type MigrondiRunner() =

    static member inline private tryGetMigrondiConfig
        (tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>> option)
        : TryGetMigrondiConfigFn =
        tryGetMigrondiConfig
        |> Option.map FuncConvert.FromFunc
        |> Option.defaultValue FileSystem.TryGetMigrondiConfig

    static member inline private getConnection
        (getConnection: Func<string, Driver, Lazy<IDbConnection>> option)
        : GetConnection =
        getConnection
        |> Option.map FuncConvert.FromFunc
        |> Option.defaultValue Queries.getConnection

    static member RunInit
        (
            options: InitOptions,
            [<Optional;DefaultParameterValue(null: Func<string, Result<string, exn>>)>] ?tryGetOrCreateDirectory: Func<string, Result<string, exn>>,
            [<Optional;DefaultParameterValue(null: Func<string, string, Result<MigrondiConfig, exn>> )>] ?tryGetOrCreateConfiguration: Func<string, string, Result<MigrondiConfig, exn>>
        ) : Result<int, exn> =

        let tryGetOrCreateDirectory =
            tryGetOrCreateDirectory
            |> Option.map FuncConvert.FromFunc
            |> Option.defaultValue FileSystem.TryGetOrCreateDirectory

        let tryGetOrCreateConfiguration =
            tryGetOrCreateConfiguration
            |> Option.map FuncConvert.FromFunc
            |> Option.defaultValue FileSystem.TryGetOrCreateConfiguration

        Migrations.tryRunMigrationsInit options tryGetOrCreateDirectory tryGetOrCreateConfiguration

    static member RunNew
        (
            options: NewOptions,
            ?config: MigrondiConfig,
            ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            ?tryCreateNewMigrationFile: Func<string, string, Result<string, exn>>
        ) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()
                }

            let tryCreateNewMigrationFile =
                tryCreateNewMigrationFile
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue FileSystem.TryCreateNewMigrationFile

            return! Migrations.tryRunMigrationsNew options config tryCreateNewMigrationFile
        }

    static member RunUp
        (
            options: UpOptions,
            ?config: MigrondiConfig,
            ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            ?runDryMigrations: Func<Driver, MigrationType, MigrationFile array, Tuple<string, IDictionary<string, obj>, string> array>,
            ?runMigrations: Func<Driver, IDbConnection, MigrationType, MigrationFile array, Result<int array, exn>>,
            ?ensureMigrationsTable: Func<Driver, IDbConnection, Result<unit, exn>>,
            ?getLastMigration: Func<IDbConnection, Result<Migration option, exn>>,
            ?getMigrationsFromDisk: Func<string, MigrationFile array>
        ) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()
                }

            let runDryMigrations =
                runDryMigrations
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.dryRunMigrations

            let runMigrations =
                runMigrations
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.runMigrations

            let ensureMigrationsTable =
                ensureMigrationsTable
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.ensureMigrationsTable

            let getLastMigration =
                getLastMigration
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.getLastMigration

            let getMigrationsFromDisk =
                getMigrationsFromDisk
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue FileSystem.GetMigrations

            return!
                Migrations.tryRunMigrationsUp
                    options
                    config
                    Queries.initializeDriver
                    (MigrondiRunner.getConnection getConnection)
                    Queries.getPendingMigrations
                    runDryMigrations
                    runMigrations
                    ensureMigrationsTable
                    getLastMigration
                    getMigrationsFromDisk
        }

    static member RunDown
        (
            options: DownOptions,
            ?config: MigrondiConfig,
            ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            ?runDryMigrations: Func<Driver, MigrationType, MigrationFile array, Tuple<string, IDictionary<string, obj>, string> array>,
            ?runMigrations: Func<Driver, IDbConnection, MigrationType, MigrationFile array, Result<int array, exn>>,
            ?ensureMigrationsTable: Func<Driver, IDbConnection, Result<unit, exn>>,
            ?getLastMigration: Func<IDbConnection, Result<Migration option, exn>>,
            ?getMigrationsFromDisk: Func<string, MigrationFile array>
        ) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()
                }

            let runDryMigrations =
                runDryMigrations
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.dryRunMigrations

            let runMigrations =
                runMigrations
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.runMigrations

            let ensureMigrationsTable =
                ensureMigrationsTable
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.ensureMigrationsTable

            let getLastMigration =
                getLastMigration
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.getLastMigration

            let getMigrationsFromDisk =
                getMigrationsFromDisk
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue FileSystem.GetMigrations

            return!
                Migrations.tryRunMigrationsDown
                    options
                    config
                    Queries.initializeDriver
                    (MigrondiRunner.getConnection getConnection)
                    Queries.getAppliedMigrations
                    runDryMigrations
                    runMigrations
                    ensureMigrationsTable
                    getLastMigration
                    getMigrationsFromDisk
        }

    static member RunList
        (
            options: ListOptions,
            ?config: MigrondiConfig,
            ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            ?getLastMigration: Func<IDbConnection, Result<Migration option, exn>>,
            ?getMigrationsFromDisk: Func<string, MigrationFile array>
        ) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()
                }


            let getLastMigration =
                getLastMigration
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.getLastMigration

            let getMigrationsFromDisk =
                getMigrationsFromDisk
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue FileSystem.GetMigrations

            return!
                Migrations.tryListMigrations
                    options
                    config
                    Queries.initializeDriver
                    (MigrondiRunner.getConnection getConnection)
                    Queries.getAppliedMigrations
                    Queries.getPendingMigrations
                    getLastMigration
                    getMigrationsFromDisk
        }

    static member RunStatus
        (
            options: StatusOptions,
            ?config: MigrondiConfig,
            ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            ?tryGetByFilename: Func<IDbConnection, string, Result<bool, exn>>
        ) : Result<int, exn> =
        result {
            let! config =
                result {
                    match config with
                    | Some config -> return config
                    | None -> return! FileSystem.TryGetMigrondiConfig()
                }

            let getConnection: GetConnection =
                getConnection
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.getConnection

            let tryGetByFilename: TryGetByFilename =
                tryGetByFilename
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue Queries.tryGetByFilename

            return!
                Migrations.truRunMigrationsStatus options config Queries.initializeDriver getConnection tryGetByFilename
        }
