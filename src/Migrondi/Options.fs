namespace Migrondi.Options

open Argu

type InitOptions =
    { path: string
      noColor: bool
      json: bool }

type NewOptions =
    { name: string
      noColor: bool
      json: bool }

type UpOptions =
    { total: int
      dryRun: bool
      noColor: bool
      json: bool }

type DownOptions =
    { total: int
      dryRun: bool
      noColor: bool
      json: bool }

type MigrationListEnum =
    | Present = 1
    | Pending = 2
    | Both = 3

type ListOptions =
    { listKind: MigrationListEnum
      amount: int
      noColor: bool
      json: bool }

type StatusOptions =
    { filename: string
      noColor: bool
      json: bool }

module Cli =

    [<RequireQualifiedAccess>]
    type InitArgs =
        | [<AltCommandLine("-p")>] Path of string option

        interface IArgParserTemplate with
            member this.Usage: string =
                match this with
                | Path _ -> "Where should the migrondi.json should be created."

        static member GetOptions(results: ParseResults<InitArgs>, ?noColor: bool, ?asJson: bool) : InitOptions =
            let defaultPath = "./migrations/"
            { path = defaultArg (results.TryGetResult(Path) |> Option.flatten) defaultPath
              noColor = defaultArg noColor false
              json = defaultArg asJson false }

    [<RequireQualifiedAccess>]
    type NewArgs =
        | [<AltCommandLine("-n"); Mandatory>] Name of string

        interface IArgParserTemplate with
            member this.Usage: string =
                match this with
                | Name _ -> "Friendly Name of the Migration you want to create."

        static member GetOptions(results: ParseResults<NewArgs>, ?noColor: bool, ?asJson: bool) : NewOptions =
            { name = results.GetResult(Name)
              noColor = defaultArg noColor false
              json = defaultArg asJson false }

    [<RequireQualifiedAccess>]
    type UpArgs =
        | [<AltCommandLine("-t")>] Total of int option
        | [<AltCommandLine("-d")>] Dry_Run of bool option

        interface IArgParserTemplate with
            member this.Usage: string =
                match this with
                | Total _ -> "Total amount of migrations to run."
                | Dry_Run _ -> "Prints to the console what is going to be run against the database."

        static member GetOptions(results: ParseResults<UpArgs>, ?noColor: bool, ?asJson: bool) : UpOptions =
            { total = defaultArg (results.TryGetResult(Total) |> Option.flatten) 0
              dryRun = defaultArg (results.TryGetResult(Dry_Run) |> Option.flatten) true
              noColor = defaultArg noColor false
              json = defaultArg asJson false }

    [<RequireQualifiedAccess>]
    type DownArgs =
        | [<AltCommandLine("-t")>] Total of int option
        | [<AltCommandLine("-d")>] Dry_Run of bool option

        interface IArgParserTemplate with
            member this.Usage: string =
                match this with
                | Total _ -> "Total amount of migrations to run."
                | Dry_Run _ -> "Prints to the console what is going to be run against the database."

        static member GetOptions(results: ParseResults<DownArgs>, ?noColor: bool, ?asJson: bool) : DownOptions =
            { total = defaultArg (results.TryGetResult(Total) |> Option.flatten) 0
              dryRun = defaultArg (results.TryGetResult(Dry_Run) |> Option.flatten) true
              noColor = defaultArg noColor false
              json = defaultArg asJson false }

    [<RequireQualifiedAccess>]
    type ListArgs =
        | [<AltCommandLine("-n")>] Amount of int option
        | [<AltCommandLine("-k")>] Kind of MigrationListEnum option

        interface IArgParserTemplate with
            member this.Usage: string =
                match this with
                | Kind _ -> "Which migrations should be listed, defaults to \"pending\"."
                | Amount _ -> "Amount of migrations to get, defaults to 5."

        static member GetOptions(results: ParseResults<ListArgs>, ?noColor: bool, ?asJson: bool) : ListOptions =
            { amount = defaultArg (results.TryGetResult(Amount) |> Option.flatten) -1
              listKind = defaultArg (results.TryGetResult(Kind) |> Option.flatten) MigrationListEnum.Pending
              noColor = defaultArg noColor false
              json = defaultArg asJson false }

    type StatusArgs =
        | [<AltCommandLine("-n"); Mandatory>] Name of string

        interface IArgParserTemplate with
            member this.Usage: string =
                match this with
                | Name _ -> "Name of the file to check against the database."

        static member GetOptions(results: ParseResults<StatusArgs>, ?noColor: bool, ?asJson: bool) : StatusOptions =
            { filename = results.GetResult(Name)
              noColor = defaultArg noColor false
              json = defaultArg asJson false }

    type MigrondiArgs =
        | [<CliPrefix(CliPrefix.None)>] Init of ParseResults<InitArgs>
        | [<CliPrefix(CliPrefix.None)>] New of ParseResults<NewArgs>
        | [<CliPrefix(CliPrefix.None)>] Up of ParseResults<UpArgs>
        | [<CliPrefix(CliPrefix.None)>] Down of ParseResults<DownArgs>
        | [<CliPrefix(CliPrefix.None)>] List of ParseResults<ListArgs>
        | [<CliPrefix(CliPrefix.None)>] Status of ParseResults<StatusArgs>
        | [<First; AltCommandLine("-nc")>] No_Color of bool option
        | [<First; AltCommandLine("-j")>] Json of bool option

        interface IArgParserTemplate with
            member this.Usage: string =
                match this with
                | Init _ -> "Creates basic files and directories to start using migrondi."
                | New _ -> "Creates a new Migration file."
                | Up _ -> "Runs the migrations against the database."
                | Down _ -> "Rolls back migrations from the database."
                | List _ -> "List the amount of migrations in the database."
                | Status _ ->
                    "Checks if a migration file is present in the database, this file has to be inside \"migrationsDir\" from migrondi.json"
                | No_Color _ -> "Write to the console without coloring enabled."
                | Json _ -> "Output to the console with a json format."
