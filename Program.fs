open CommandLine
open FSharp.Data.Dapper
open Sqlator.Options
open Sqlator.Migrations

[<EntryPoint>]
let main argv =
  OptionHandler.RegisterTypes()
  let result = CommandLine.Parser.Default.ParseArguments<NewOptions, UpOptions, DownOptions, ListOptions>(argv)
  match result with
  | :? Parsed<obj> as command ->
    match command.Value with 
    | :? NewOptions as newOptions ->
        try 
            runMigrationsNew newOptions
        with
        | :? System.UnauthorizedAccessException ->
                printfn "Failed to write migration to disk"
                printfn "Check that you have sufficient permission on the directory"
    | :? UpOptions as upOptions ->
            let result = asyncRunMigrationsUp upOptions |> Async.RunSynchronously
            match result with 
            | Choice1Of2 result ->
                printfn "Migrations Applied: %i" result.Length
            | Choice2Of2 ex ->
                match ex with 
                | :? System.UnauthorizedAccessException as ex ->
                     printfn "Failed to write migration to disk"
                     printfn "Check that you have sufficient permission on the directory"
                     printfn "%s" ex.Message
                | :? System.ArgumentException as ex ->
                     printfn "%s" ex.Message
                | ex ->
                        printfn "Unexpected Exception %s" ex.Message
    | :? DownOptions as downOptions ->
            let result = runMigrationsDown downOptions |> Async.RunSynchronously
            match result with 
            | Choice1Of2 result ->
                printfn "Rolled back %i migrations" result.Length
            | Choice2Of2 ex ->
                match ex with 
                | :? System.InvalidOperationException as ex ->
                     printfn "%s" ex.Message
                | ex ->
                        printfn "Unexpected Exception %s" ex.Message
    | :? ListOptions as listOptions ->
            let result = runMigrationsList listOptions |> Async.RunSynchronously
            match result with 
            | Choice1Of2 _ -> ()
            | Choice2Of2 ex ->
                match ex with 
                | :? System.UnauthorizedAccessException as ex ->
                     printfn "Failed to read migrations directory"
                     printfn "Check that you have sufficient permission on the directory"
                | ex ->
                        printfn "Unexpected Exception %s" ex.Message
    | _ -> invalidOp "Unexpected parsing result"
  | :? NotParsed<obj> as notParsed ->
    printfn "Not Parsed: %A" (notParsed.Errors |> Seq.map(fun err -> err.Tag))
  | _ -> invalidOp "Unexpected parsing result"

  0