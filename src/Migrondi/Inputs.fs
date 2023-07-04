namespace Migrondi.Inputs

open System.CommandLine
open System.CommandLine.Invocation
open System.CommandLine.Parsing
open System.IO


open FSharp.SystemCommandLine



open Migrondi.Core
open Migrondi.Core.Migrondi


[<RequireQualifiedAccess>]
module Init =

  let path =
    Input.ArgumentMaybe<DirectoryInfo>(
      "path",
      "path inizialize a migrondi configuration file"
    )

[<RequireQualifiedAccess>]
module SharedArguments =

  let name description =
    let description = defaultArg description "Friendly name of the migration"
    Input.Argument<string>("name", description)

  let amount =
    Input.ArgumentMaybe<int>(
      "amount",
      "Amount of migrations to run against the database"
    )

  let isDry =
    Input.OptionMaybe<bool>(
      [ "-d"; "--dry" ],
      "Signals migrondi to not run against the database and prints the migrations that would be run into the screen"
    )

[<RequireQualifiedAccess>]
module ListArgs =
  let migrationKind =
    let op =
      let parseArgument (result: ArgumentResult) =
        match result.Tokens |> Seq.tryHead with
        | Some token when token.Value.ToLowerInvariant() = "up" ->
          MigrationType.Up
        | Some token when token.Value.ToLowerInvariant() = "down" ->
          MigrationType.Down
        | _ -> MigrationType.Up

      Option<MigrationType>(
        [| "-k"; "--kind" |],
        parseArgument = parseArgument,
        Description = ""
      )

    op