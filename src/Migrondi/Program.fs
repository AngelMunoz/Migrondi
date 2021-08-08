open Argu

open FsToolkit.ErrorHandling

open Migrondi.Types
open Migrondi.Options.Cli
open Migrondi.Migrations

[<EntryPoint>]
let main argv =
    let getCommand () : Result<MigrondiArgs * bool * bool, exn> =
        result {
            let parser = ArgumentParser.Create<MigrondiArgs>()
            let parsed = parser.Parse argv

            let json =
                parsed.TryGetResult(MigrondiArgs.Json)
                |> Option.map
                    (fun opt ->
                        if opt.IsNone then
                            true
                        else
                            opt |> Option.defaultValue false)
                |> Option.defaultValue false

            let noColor =
                parsed.TryGetResult(MigrondiArgs.No_Color)
                |> Option.map
                    (fun opt ->
                        if opt.IsNone then
                            true
                        else
                            opt |> Option.defaultValue false)
                |> Option.defaultValue false

            let cliArgs =
                parsed.GetAllResults()
                |> List.filter
                    (fun result ->
                        match result with
                        | Json _
                        | No_Color _ -> false
                        | _ -> true)

            match cliArgs with
            | [ Init subcmd ] -> return Init subcmd, noColor, json
            | [ New subcmd ] -> return New subcmd, noColor, json
            | [ Up subcmd ] -> return Up subcmd, noColor, json
            | [ Down subcmd ] -> return Down subcmd, noColor, json
            | _ -> return! CommandNotParsedException |> Result.Error
        }

    result {
        let! command = getCommand ()

        return!
            match command with
            | (MigrondiArgs.Init args, noColor, json) ->
                InitArgs.GetOptions(args, noColor, json)
                |> MigrondiRunner.RunInit
            | (MigrondiArgs.New args, noColor, json) ->
                NewArgs.GetOptions(args, noColor, json)
                |> MigrondiRunner.RunNew
            | (MigrondiArgs.Up args, noColor, json) ->
                UpArgs.GetOptions(args, noColor, json)
                |> MigrondiRunner.RunUp
            | (MigrondiArgs.Down args, noColor, json) ->
                DownArgs.GetOptions(args, noColor, json)
                |> MigrondiRunner.RunDown
            | (MigrondiArgs.List args, noColor, json) ->
                ListArgs.GetOptions(args, noColor, json)
                |> MigrondiRunner.RunList
            | _ -> CommandNotParsedException |> Result.Error
    }
    |> function
        | Ok exitCode -> exitCode
        | Error ex ->
            eprintfn "%O" ex
            1
