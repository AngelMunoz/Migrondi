open CommandLine

open FsToolkit.ErrorHandling

open Migrondi.Types
open Migrondi.FileSystem
open Migrondi.Options
open Migrondi.Migrations
open Migrondi.Queries

[<EntryPoint>]
let main argv =
    let parsedResult () =
        match
            CommandLine.Parser.Default.ParseArguments<InitOptions, NewOptions, UpOptions, DownOptions, ListOptions>
                (argv)
            with
        | :? (Parsed<obj>) as command -> Ok command
        | _ -> (Result.Error CommandNotParsedException)

    result {

        let! config = FileSystem.TryGetMigrondiConfig()

        let! command = parsedResult ()

        match command.Value with
        | :? InitOptions as initOptions ->
            return!
                tryRunMigrationsInit
                    FileSystem.TryGetOrCreateDirectory
                    FileSystem.TryGetOrCreateConfiguration
                    initOptions
        | :? NewOptions as newOptions ->
            return! tryRunMigrationsNew FileSystem.TryCreateNewMigrationFile config newOptions
        | :? UpOptions as upOptions ->
            return! tryRunMigrationsUp upOptions config getConnection FileSystem.GetMigrations
        | :? DownOptions as downOptions ->
            return! tryRunMigrationsDown downOptions config getConnection FileSystem.GetMigrations
        | :? ListOptions as listOptions ->
            return! tryRunMigrationsList listOptions config getConnection FileSystem.GetMigrations
        | parsed -> return! (Result.Error(InvalidOptionSetException(nameof parsed)))
    }
    |> function
        | Ok exitCode -> exitCode
        | Error ex ->
            Spectre.Console.AnsiConsole.WriteException ex
            1
