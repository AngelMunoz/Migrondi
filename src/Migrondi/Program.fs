open CommandLine

open FsToolkit.ErrorHandling

open Migrondi.Types
open Migrondi.Options
open Migrondi.Migrations

[<EntryPoint>]
let main argv =
    let getParsedResult () =
        result {
            let! parsed =
                try
                    CommandLine.Parser.Default.ParseArguments<InitOptions, NewOptions, UpOptions, DownOptions, ListOptions>(
                        argv
                    )
                    |> Ok
                with
                | ex -> Result.Error CommandNotParsedException

            match parsed with
            | :? (Parsed<obj>) as command -> return command
            | _ -> return! (Result.Error CommandNotParsedException)
        }

    result {
        let! command = getParsedResult ()

        match command.Value with
        | :? InitOptions as options -> return! MigrondiRunner.RunInit(options)
        | :? NewOptions as options -> return! MigrondiRunner.RunNew(options)
        | :? UpOptions as options -> return! MigrondiRunner.RunUp(options)
        | :? DownOptions as options -> return! MigrondiRunner.RunDown(options)
        | :? ListOptions as options -> return! MigrondiRunner.RunList(options)
        | parsed -> return! (Result.Error(InvalidOptionSetException(nameof parsed)))
    }
    |> function
        | Ok exitCode -> exitCode
        | Error ex ->
            eprintfn "%O" ex
            1
