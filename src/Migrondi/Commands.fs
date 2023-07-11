namespace Migrondi.Commands

open System
open System.IO

open System.CommandLine.Invocation

open FSharp.SystemCommandLine
open Migrondi.Env


[<RequireQualifiedAccess>]
module ArgumentMapper =
  open Migrondi.Handlers
  open Migrondi.Core


  let Init (appEnv: AppEnv) (dir: DirectoryInfo option) =
    let path =
      match dir with
      | Some directory -> directory
      | None -> Directory.GetCurrentDirectory() |> DirectoryInfo

    path, appEnv.Migrondi

  let Up (appEnv: AppEnv) (amount: int option, isDry: bool option) =
    match isDry with
    | Some true -> Migrations.runDryUp(amount, appEnv.Migrondi)
    | Some false
    | None -> Migrations.runUp(amount, appEnv.Migrondi)

  let Down (appEnv: AppEnv) (amount: int option, isDry: bool option) =
    match isDry with
    | Some true -> Migrations.runDryDown(amount, appEnv.Migrondi)
    | Some false
    | None -> Migrations.runDown(amount, appEnv.Migrondi)

  let New (appEnv: AppEnv) (name: string) = name, appEnv.Migrondi

  let List (appEnv: AppEnv) (kind: MigrationType) = kind, appEnv.Migrondi

  let Status (appEnv: AppEnv) (name: string) = name, appEnv.Migrondi

[<RequireQualifiedAccess>]
module Commands =
  open Migrondi.Inputs
  open Migrondi.Handlers


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