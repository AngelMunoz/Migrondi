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

/// A function that should initialize the migrondi workspace by creating a configuration file and a migrations directory
type TryRunInit = InitOptions -> TryGetOrCreateDirectoryFn -> TryGetOrCreateConfigFn -> Result<int, exn>

/// A function that should create a new SQL migration file using the provided options and configuration
type TryRunMigrationsNew = NewOptions -> MigrondiConfig -> TryCreateFileFn -> Result<int, exn>

/// A function that should run the pending migration files against the database using the provided options and configuration
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

/// A function that should roll back the existing migrations in the database using the provided options and configuration
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

/// A function that should present a list of migration based on if its present or not in the database or both kinds of migrations, using the provided options and configuration
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

/// A function that should check against the database to see if a particular file is present in the database
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

    /// <summary>
    ///   Creates a "migrondi.json" file and a "migrations" directory on the given path,
    ///   or the current working directory if a path is not specified
    ///</summary>
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

    /// <summary>
    ///   Creates a &lt;migration name&gt;_&lt;timestamp&gt;.sql file inside
    ///   the directory provided by the migrondi configuration.
    /// </summary>
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

    /// Runs a set of migrations against the database or print to the console if a "dry-run" is specified
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

    /// Rolls back a set of migrations from the database or print to the console if a "dry-run" is specified
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

    /// checks the database and tries to list if migrations are present, missing or both depending on the supplied options
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

/// This is the default migrondi runner takes all of the existing functions
/// and wires them together inside each run method, it allows you to supply your own
/// functions in case you need to customize or modify the way things are done by migrondi
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

    /// <summary>Initializes a migrondi workspace creating a configuration file and a migrations directory </summary>
    /// <param name="options">
    ///   The initialization options, the path is where the migrondi workspace will be created, if not supplied
    ///   the current working directory will be used to create the migrondi workspace
    ///  </param>
    /// <param name="tryGetOrCreateDirectory">
    ///   A custom function to generate a directory, it takes a path like string and returns the result of the operation
    /// </param>
    /// <param name="tryGetOrCreateConfiguration">
    ///   A custom function to generate a the configuration file
    ///   it takes a string: filename and a path like string: migrationsDir
    /// </param>
    static member RunInit
        (
            options: InitOptions,
            [<Optional>] ?tryGetOrCreateDirectory: Func<string, Result<string, exn>>,
            [<Optional>] ?tryGetOrCreateConfiguration: Func<string, string, Result<MigrondiConfig, exn>>
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

    /// <summary>Creates a new SQL migration file &lt;filename&gt;_&lt;timestamp&gt;.sql</summary>
    /// <param name="options">
    ///   provides the new migration file creation options
    ///  </param>
    /// <param name="tryGetMigrondiConfig">
    ///   A function to get a migrondi configuration object,
    ///   if not provided the library will try to find a migrondi.json file
    ///   in the current working directory
    /// </param>
    /// <param name="tryCreateNewMigrationFile">
    ///   A custom function to generate a the configuration file
    ///   it takes a string: path like string (e.g. "./migrations") and the supplied filename from the options
    /// </param>
    static member RunNew
        (
            options: NewOptions,
            [<Optional>] ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            [<Optional>] ?tryCreateNewMigrationFile: Func<string, string, Result<string, exn>>
        ) : Result<int, exn> =
        result {
            let! config = (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()

            let tryCreateNewMigrationFile =
                tryCreateNewMigrationFile
                |> Option.map FuncConvert.FromFunc
                |> Option.defaultValue FileSystem.TryCreateNewMigrationFile

            return! Migrations.tryRunMigrationsNew options config tryCreateNewMigrationFile
        }

    /// <summary>Run the current pending migrations against the database</summary>
    /// <param name="options">
    ///   provides the run options, like amount to run (0 for all) and if it should be a "dry" run or not
    ///  </param>
    /// <param name="tryGetMigrondiConfig">
    ///   A function to get a migrondi configuration object,
    ///   if not provided the library will try to find a migrondi.json file
    ///   in the current working directory
    /// </param>
    /// <param name="getConnection">
    ///   A function to obtain a Lazy <see cref="System.Data.IDbConnection">IDbConnection</see>
    ///   which will be reused by some stages in the internal workings of the function
    /// </param>
    /// <param name="runDryMigrations">
    ///   A custom function to run a set of "dry" migrations, e.g. a fake run against the database.
    /// </param>
    /// <param name="runMigrations">
    ///   A custom function to run a set of "live" migrations agains the database
    /// </param>
    /// <param name="ensureMigrationsTable">
    ///   A custom function to ensure that the "migration" table exists.
    ///   <remarks>Migrondi uses a table inside the database named "migration" it is required to use the same name at least for now.</remarks>
    /// </param>
    /// <param name="getLastMigration">
    ///   A custom function to query the database and try to get the last migration present in the database
    /// </param>
    /// <param name="getMigrationsFromDisk">
    ///   A custom function to obtain an array of <see cref="Migrondi.Types.MigrationFile">MigrationFile</see>s existing in the user's drive
    /// </param>
    static member RunUp
        (
            options: UpOptions,
            [<Optional>] ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            [<Optional>] ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            [<Optional>] ?runDryMigrations: Func<Driver, MigrationType, MigrationFile array, Tuple<string, IDictionary<string, obj>, string> array>,
            [<Optional>] ?runMigrations: Func<Driver, IDbConnection, MigrationType, MigrationFile array, Result<int array, exn>>,
            [<Optional>] ?ensureMigrationsTable: Func<Driver, IDbConnection, Result<unit, exn>>,
            [<Optional>] ?getLastMigration: Func<IDbConnection, Result<Migration option, exn>>,
            [<Optional>] ?getMigrationsFromDisk: Func<string, MigrationFile array>
        ) : Result<int, exn> =
        result {
            let! config = (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()

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

    /// <summary>Roll back the current existing migrations in the database</summary>
    /// <param name="options">
    ///   provides the run options, like amount to run (0 for all) and if it should be a "dry" run or not
    ///  </param>
    /// <param name="tryGetMigrondiConfig">
    ///   A function to get a migrondi configuration object,
    ///   if not provided the library will try to find a migrondi.json file
    ///   in the current working directory
    /// </param>
    /// <param name="getConnection">
    ///   A function to obtain a Lazy <see cref="System.Data.IDbConnection">IDbConnection</see>
    ///   which will be reused by some stages in the internal workings of the function
    /// </param>
    /// <param name="runDryMigrations">
    ///   A custom function to run a set of "dry" migrations, e.g. a fake run against the database.
    /// </param>
    /// <param name="runMigrations">
    ///   A custom function to run a set of "live" migrations agains the database
    /// </param>
    /// <param name="ensureMigrationsTable">
    ///   A custom function to ensure that the "migration" table exists.
    ///   <remarks>Migrondi uses a table inside the database named "migration" it is required to use the same name at least for now.</remarks>
    /// </param>
    /// <param name="getLastMigration">
    ///   A custom function to query the database and try to get the last migration present in the database
    /// </param>
    /// <param name="getMigrationsFromDisk">
    ///   A custom function to obtain an array of <see cref="Migrondi.Types.MigrationFile">MigrationFile</see>s existing in the user's drive
    /// </param>
    static member RunDown
        (
            options: DownOptions,
            [<Optional>] ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            [<Optional>] ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            [<Optional>] ?runDryMigrations: Func<Driver, MigrationType, MigrationFile array, Tuple<string, IDictionary<string, obj>, string> array>,
            [<Optional>] ?runMigrations: Func<Driver, IDbConnection, MigrationType, MigrationFile array, Result<int array, exn>>,
            [<Optional>] ?ensureMigrationsTable: Func<Driver, IDbConnection, Result<unit, exn>>,
            [<Optional>] ?getLastMigration: Func<IDbConnection, Result<Migration option, exn>>,
            [<Optional>] ?getMigrationsFromDisk: Func<string, MigrationFile array>
        ) : Result<int, exn> =
        result {
            let! config = (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()

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

    /// <summary>Run the current pending migrations against the database</summary>
    /// <param name="options">
    ///   Options to list migrations that are either pending, missing, or both in the database
    ///  </param>
    /// <param name="tryGetMigrondiConfig">
    ///   A function to get a migrondi configuration object,
    ///   if not provided the library will try to find a migrondi.json file
    ///   in the current working directory
    /// </param>
    /// <param name="getConnection">
    ///   A function to obtain a Lazy <see cref="System.Data.IDbConnection">IDbConnection</see>
    ///   which will be reused by some stages in the internal workings of the function
    /// </param>
    /// <param name="getLastMigration">
    ///   A custom function to query the database and try to get the last migration present in the database
    /// </param>
    /// <param name="getMigrationsFromDisk">
    ///   A custom function to obtain an array of <see cref="Migrondi.Types.MigrationFile">MigrationFile</see>s existing in the user's drive
    /// </param>
    static member RunList
        (
            options: ListOptions,
            [<Optional>] ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            [<Optional>] ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            [<Optional>] ?getLastMigration: Func<IDbConnection, Result<Migration option, exn>>,
            [<Optional>] ?getMigrationsFromDisk: Func<string, MigrationFile array>
        ) : Result<int, exn> =
        result {
            let! config = (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()


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
    /// <summary>
    /// Checks if a given filename of an SQL migration is present in the database
    /// </summary>
    /// <param name="options">
    ///   Options to list migrations that are either pending, missing, or both in the database
    ///  </param>
    /// <param name="tryGetMigrondiConfig">
    ///   A function to get a migrondi configuration object,
    ///   if not provided the library will try to find a migrondi.json file
    ///   in the current working directory
    /// </param>
    /// <param name="getConnection">
    ///   A function to obtain a Lazy <see cref="System.Data.IDbConnection">IDbConnection</see>
    ///   which will be reused by some stages in the internal workings of the function
    /// </param>
    /// <param name="tryGetByFilename">
    ///   A function to search the migration file in the database.
    /// </param>
    static member RunStatus
        (
            options: StatusOptions,
            [<Optional>] ?tryGetMigrondiConfig: Func<Result<MigrondiConfig, exn>>,
            [<Optional>] ?getConnection: Func<string, Driver, Lazy<IDbConnection>>,
            [<Optional>] ?tryGetByFilename: Func<IDbConnection, string, Result<bool, exn>>
        ) : Result<int, exn> =
        result {
            let! config = (MigrondiRunner.tryGetMigrondiConfig tryGetMigrondiConfig) ()

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
