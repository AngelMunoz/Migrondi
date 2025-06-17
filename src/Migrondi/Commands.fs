namespace Migrondi.Commands

open System.IO

open FSharp.SystemCommandLine

open Migrondi.Core
open Migrondi.Env
open Migrondi.Inputs
open Migrondi.Handlers

[<RequireQualifiedAccess>]
module internal ArgumentMapper =


  let inline Init (appEnv: AppEnv) (dir: DirectoryInfo option) =
    let path =
      match dir with
      | Some directory -> directory
      | None -> Directory.GetCurrentDirectory() |> DirectoryInfo

    path, appEnv.FileSystem, appEnv.Logger

  let inline Up (appEnv: AppEnv) (amount: int option, isDry: bool option) =
    match isDry with
    | Some true -> Migrations.runDryUp(amount, appEnv.Logger, appEnv.Migrondi)
    | Some false
    | None -> Migrations.runUp(amount, appEnv.Logger, appEnv.Migrondi)

  let inline Down (appEnv: AppEnv) (amount: int option, isDry: bool option) =
    match isDry with
    | Some true -> Migrations.runDryDown(amount, appEnv.Logger, appEnv.Migrondi)
    | Some false
    | None -> Migrations.runDown(amount, appEnv.Logger, appEnv.Migrondi)

  let inline New (appEnv: AppEnv) (name: string) =
    name, appEnv.Logger, appEnv.Migrondi

  let inline List (appEnv: AppEnv) (kind: MigrationType option) =
    appEnv.JsonOutput,
    appEnv.Logger,
    appEnv.MigrationSerializer,
    kind,
    appEnv.Migrondi

  let inline Status (appEnv: AppEnv) (name: string) =
    name, appEnv.Logger, appEnv.Migrondi

[<RequireQualifiedAccess>]
module internal Commands =

  let Init appEnv = command "init" {
    description
      "Creates a migrondi.json file where the comand is invoked or the path provided"

    addAlias "setup"

    inputs(Init.path)
    setHandler(ArgumentMapper.Init appEnv >> Init.handler)
  }

  let New appEnv = command "new" {
    description
      "This will create a new SQL migration file in the configured directory for migrations"

    addAlias "create"

    inputs(SharedArguments.name None)
    setHandler(ArgumentMapper.New appEnv >> Migrations.newMigration)
  }

  let Up appEnv = command "up" {
    description "Runs migrations against the configured database"
    addAlias "apply"

    inputs(SharedArguments.amount, SharedArguments.isDry)
    setHandler(ArgumentMapper.Up appEnv)
  }

  let Down appEnv = command "down" {
    description "Runs migrations against the configured database"
    addAlias "rollback"

    inputs(SharedArguments.amount, SharedArguments.isDry)
    setHandler(ArgumentMapper.Down appEnv)
  }

  let List appEnv = command "list" {
    description
      "Reads migrations files and the database to show what is the current state of the migrations"

    addAlias "show"

    inputs(ListArgs.MigrationKind)
    setHandler(ArgumentMapper.List appEnv >> Migrations.listMigrations)
  }

  let Status appEnv = command "status" {
    description
      "Checks whether the migration file has been applied or not to the database"

    addAlias "show-state"

    inputs(SharedArguments.name(Some "Name of the migration file"))
    setHandler(ArgumentMapper.Status appEnv >> Migrations.migrationStatus)
  }