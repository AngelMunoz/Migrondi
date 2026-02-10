namespace Migrondi.Inputs

open System.CommandLine
open System.CommandLine.Parsing
open System.IO

open FSharp.SystemCommandLine

open Migrondi.Core


[<RequireQualifiedAccess>]
module Init =

  let path =
    Input.argumentMaybe<DirectoryInfo> "path"
    |> Input.desc "path to initialize a migrondi configuration file"

[<RequireQualifiedAccess>]
module SharedArguments =

  let name description =
    let description = defaultArg description "Friendly name of the migration"
    Input.argument<string> "name" |> Input.desc description

  let amount =
    Input.argumentMaybe<int> "amount"
    |> Input.desc "Amount of migrations to run against the database"

  let isDry =
    Input.optionMaybe<bool> "--dry"
    |> Input.alias "-d"
    |> Input.desc
      "Signals migrondi to not run against the database and prints the migrations that would be run into the screen"

  let manualTransaction =
    Input.optionMaybe<bool> "--manual-transaction"
    |> Input.alias "-m"
    |> Input.desc
      "Signals migrondi to create a migration that will not be wrapped in a transaction when executed"

[<RequireQualifiedAccess>]
module ListArgs =
  let MigrationKind =
    let op =
      let parseArgument (result: ArgumentResult) =
        match result.Tokens |> Seq.tryHead with
        | Some token when token.Value.ToLowerInvariant() = "up" ->
          Some MigrationType.Up
        | Some token when token.Value.ToLowerInvariant() = "down" ->
          Some MigrationType.Down
        | _ -> None

      Option<MigrationType option>(
        "kind",
        [| "-k"; "--kind" |],
        CustomParser = parseArgument,
        Description = ""
      )
        .AcceptOnlyFromAmong([| "up"; "down" |])


    Input.ofOption op