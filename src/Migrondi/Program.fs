open CommandLine
open Migrondi.Types
open Migrondi.Options
open Migrondi.Migrations
open Migrondi.Utils
open System.Data
open UserInterface


let initializeDriver driver =
    match driver with
    | Driver.Mssql -> RepoDb.SqlServerBootstrap.Initialize()
    | Driver.Sqlite -> RepoDb.SqLiteBootstrap.Initialize()
    | Driver.Mysql -> RepoDb.MySqlBootstrap.Initialize()
    | Driver.Postgresql -> RepoDb.PostgreSqlBootstrap.Initialize()

let tryRunInit init =
    try
        runMigrationsInit init
        Ok()
    with ex -> Result.Error ex

let tryRunNew (getConfig: unit -> Result<string * MigrondiConfig * Driver, exn>) newOptions =

    try
        match getConfig () with
        | Ok config ->
            runMigrationsNew config newOptions
            Ok()
        | Error ex -> Result.Error ex
    with :? System.UnauthorizedAccessException ->
        Result.Error(
            exn "Failed to write migration to disk\nCheck that you have sufficient permission on the directory"
        )

let tryRunUp (getConfig: unit -> Result<string * MigrondiConfig * Driver, exn>)
                (getConnection: Driver -> string -> IDbConnection)
                upOptions
                =
    try
        match getConfig () with
        | Ok (path, config, driver) ->
            initializeDriver driver
            let connection = getConnection driver config.connection

            let result =
                (runMigrationsUp connection (path, config, driver) upOptions)
                    .Length

            connection.Close()
            Ok result

        | Error err -> Result.Error err
    with
    | :? System.UnauthorizedAccessException as ex ->
        let l1 = "Failed to write migration to disk"

        let l2 =
            "Check that you have sufficient permission on the directory"

        let l3 = ex.Message
        Result.Error(exn $"{l1}\n{l2}\n{l3}")
    | ex -> Result.Error ex

let tryRunDown (getConfig: unit -> Result<string * MigrondiConfig * Driver, exn>)
                (getConnection: Driver -> string -> IDbConnection)
                downOptions
                =
    try
        match getConfig () with
        | Ok (path, config, driver) ->
            initializeDriver driver
            let connection = getConnection driver config.connection

            let result =
                (runMigrationsDown connection (path, config, driver) downOptions)
                    .Length

            connection.Close()
            Ok result
        | Error err -> Result.Error err
    with ex -> Result.Error ex

let tryRunList (getConfig: unit -> Result<string * MigrondiConfig * Driver, exn>)
                (getConnection: Driver -> string -> IDbConnection)
                listOptions
                =
    try
        match getConfig () with
        | Ok (path, config, driver) ->
            initializeDriver driver
            let connection = getConnection driver config.connection
            runMigrationsList connection (path, config, driver) listOptions
            connection.Close()
            Ok()
        | Error err -> Result.Error err
    with
    | :? System.UnauthorizedAccessException ->
        Result.Error(
            exn "Failed to read migrations directory\nCheck that you have sufficient permission on the directory"
        )
    | ex -> Result.Error ex

let tryGetConfig () =
    try
        Ok(getPathConfigAndDriver ())
    with
    | :? System.IO.FileNotFoundException
    | :? System.ArgumentException
    | :? System.ArgumentNullException
    | :? System.IO.DirectoryNotFoundException
    | :? System.IO.IOException
    | :? System.IO.PathTooLongException
    | :? System.NotSupportedException
    | :? System.UnauthorizedAccessException as ex -> Result.Error ex
    | :? System.Text.Json.JsonException ->
        Result.Error(
            exn
                "Failed to parse the migrondi.json file, please check for trailing commas and that the properties have the correct name"
        )



[<EntryPoint>]
let main argv =
    printfn "%s" System.Environment.CurrentDirectory
    let result =
        CommandLine.Parser.Default.ParseArguments<InitOptions, NewOptions, UpOptions, DownOptions, ListOptions>(
            argv
        )

    match result with
    | :? (NotParsed<obj>) -> 1
    | :? (Parsed<obj>) as command ->
        match command.Value with
        | :? NewOptions as newOptions ->
            match tryRunNew tryGetConfig newOptions with
            | Ok _ -> 0
            | Error err ->
                failurePrint $"{err.Message}"
                1
        | :? UpOptions as upOptions ->
            match tryRunUp tryGetConfig getConnection upOptions with
            | Ok amount ->
                match upOptions.dryRun with
                | true -> successPrint $"Total Migrations To Run: %i{amount}"
                | false -> successPrint $"Migrations Applied: %i{amount}"

                0
            | Error err ->
                failurePrint $"{err.Message}"
                1
        | :? DownOptions as downOptions ->
            match tryRunDown tryGetConfig getConnection downOptions with
            | Ok amount ->
                match downOptions.dryRun with
                | true -> successPrint $"Total Migrations To Run: %i{amount}"
                | false -> successPrint $"Rolled back %i{amount} migrations"

                0
            | Error err ->
                failurePrint $"{err.Message}"
                1
        | :? ListOptions as listOptions ->
            match tryRunList tryGetConfig getConnection listOptions with
            | Ok _ -> 0
            | Error err ->
                failurePrint $"{err.Message}"
                1
        | :? InitOptions as initOptions ->
            let result = tryRunInit initOptions

            match result with
            | Ok _ -> 0
            | Error err ->
                failurePrint $"{err.Message}"
                1
        | _ ->
            failurePrint "Unexpected parsing result"
            1
    | _ ->
        failurePrint "Unexpected parsing result"
        1
