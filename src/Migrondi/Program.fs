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

            let! parsed =
                try
                    parser.Parse argv |> Ok
                with
                | :? Argu.ArguParseException as ex -> CommandNotParsedException(ex.Message) |> Error


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
            | [ List subcmd ] -> return List subcmd, noColor, json
            | [ Status subcmd ] -> return Status subcmd, noColor, json
            | args ->
                return!
                    CommandNotParsedException $"%A{args}"
                    |> Result.Error
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
            | (MigrondiArgs.Status args, noColor, json) ->
                StatusArgs.GetOptions(args, noColor, json)
                |> MigrondiRunner.RunStatus
            | args ->
                CommandNotParsedException $"%A{args}"
                |> Result.Error
    }
    |> function
        | Ok exitCode -> exitCode
        | Error ex ->
            match ex with
            | InvalidDriverException message
            | EmptyPath message
            | ConfigurationExists message
            | ConfigurationNotFound message
            | InvalidMigrationName message
            | FailedToReadFile message
            | FailedToExecuteQuery message
            | InvalidOptionSetException message
            | CommandNotParsedException message
            | AmountExeedsExistingException message -> eprintfn "%s" message
            | MigrationApplyFailedException (message, file, driver) ->
                eprintfn $"Failed to apply {file.name} with {driver.AsString()}: {message}"
            | MissingMigrationContent content ->
                eprintfn $"The migration file is corrupt or missing parts, found: %A{content}"
            | others -> eprintfn "%s, at %s" others.Message others.Source

            1
