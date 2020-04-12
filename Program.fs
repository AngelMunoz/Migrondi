open CommandLine
open Sqlator.Types
open Sqlator.Options
open Sqlator.Migrations
open Sqlator.Utils
open FSharp.Control.Tasks.V2

let initializeDriver driver = 
    match driver with
    | Driver.Mssql -> RepoDb.SqlServerBootstrap.Initialize()
    | Driver.Sqlite -> RepoDb.SqLiteBootstrap.Initialize()
    | Driver.Mysql -> RepoDb.MySqlBootstrap.Initialize()
    | Driver.Postgresql -> RepoDb.PostgreSqlBootstrap.Initialize()

[<EntryPoint>]
let main argv =
  let (path, config, driver) = 
    try
        getPathConfigAndDriver()
    with
    | :? System.IO.FileNotFoundException as ex ->
        printfn "%s" ex.Message
        exit(1)
    | :? System.ArgumentException as ex ->
        printfn "%s" ex.Message
        exit(1)
    | :? System.Text.Json.JsonException->
        printfn "Failed to parse the sqlator.json file, please check for trailing commas and that the properties have the correct name"
        exit(1)
  initializeDriver driver
  let connection = getConnection driver config.connection
  let result = CommandLine.Parser.Default.ParseArguments<NewOptions, UpOptions, DownOptions, ListOptions>(argv)
  match result with
  | :? Parsed<obj> as command ->
    match command.Value with 
    | :? NewOptions as newOptions ->
        try 
            runMigrationsNew (path, config, driver) newOptions
        with
        | :? System.UnauthorizedAccessException ->
                printfn "Failed to write migration to disk"
                printfn "Check that you have sufficient permission on the directory"
    | :? UpOptions as upOptions ->
        try 
            let result = runMigrationsUp connection (path,config, driver) upOptions
            printfn "Migrations Applied: %i" result.Length
        with
        | :? System.UnauthorizedAccessException as ex ->
             printfn "Failed to write migration to disk"
             printfn "Check that you have sufficient permission on the directory"
             printfn "%s" ex.Message
        | :? System.ArgumentException as ex ->
             printfn "%s" ex.Message
        | ex ->
                printfn "Unexpected Exception %s" ex.Message
    | :? DownOptions as downOptions ->
        try
            let result = runMigrationsDown connection (path,config, driver) downOptions
            printfn "Rolled back %i migrations" result.Length
        with 
        | :? System.InvalidOperationException as ex ->
             printfn "%s" ex.Message
        | ex ->
                printfn "Unexpected Exception %s" ex.Message
    | :? ListOptions as listOptions ->
        try
            runMigrationsList connection (path,config, driver) listOptions
        with 
        | :? System.UnauthorizedAccessException as ex ->
             printfn "Failed to read migrations directory"
             printfn "Check that you have sufficient permission on the directory"
        | ex ->
                printfn "Unexpected Exception %s" ex.Message
    | _ -> invalidOp "Unexpected parsing result"
  | :? NotParsed<obj> as notParsed ->
    printfn "Not Parsed: %A" (notParsed.Errors |> Seq.map(fun err -> err.Tag))
  | _ -> invalidOp "Unexpected parsing result"
  connection.Close()
  0