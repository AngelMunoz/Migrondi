namespace Migrondi.Commands

open System.IO

open FSharp.SystemCommandLine

open Migrondi.Core
open Migrondi.Env
open Migrondi.Inputs
open Migrondi.Handlers

[<RequireQualifiedAccess>]
module internal ArgumentMapper =


  let Init
    (appEnv: AppEnv)
    (dir: DirectoryInfo option, force: bool option, merge: bool option)
    =
    let path =
      match dir with
      | Some directory -> directory
      | None -> Directory.GetCurrentDirectory() |> DirectoryInfo

    path,
    appEnv.FileSystem,
    appEnv.ConfigurationSerializer,
    appEnv.Logger,
    force,
    merge

  let Up (appEnv: AppEnv) (amount: int option, isDry: bool option) =
    match isDry with
    | Some true -> Migrations.runDryUp(amount, appEnv.Logger, appEnv.Migrondi)
    | Some false
    | None -> Migrations.runUp(amount, appEnv.Logger, appEnv.Migrondi)

  let Down (appEnv: AppEnv) (amount: int option, isDry: bool option) =
    match isDry with
    | Some true -> Migrations.runDryDown(amount, appEnv.Logger, appEnv.Migrondi)
    | Some false
    | None -> Migrations.runDown(amount, appEnv.Logger, appEnv.Migrondi)

  let inline New
    (appEnv: AppEnv)
    (name: string, manualTransaction: bool option)
    =
    name, manualTransaction, appEnv.Logger, appEnv.Migrondi

  let inline List (appEnv: AppEnv) (kind: MigrationType option) =
    appEnv.JsonOutput, appEnv.Logger, kind, appEnv.Migrondi

  let inline Status (appEnv: AppEnv) (name: string) =
    name, appEnv.Logger, appEnv.Migrondi

[<RequireQualifiedAccess>]
module internal Commands =
  open Microsoft.Extensions.Logging

  let setup(appEnv: AppEnv) =
    let db = appEnv.Database

    try
      db.SetupDatabase()
    with :? SetupDatabaseFailed as ex ->
      appEnv.Logger.LogError("Database was not setup", ex)
      reraise()

  let withSetup (appEnv: AppEnv) (action: 'args -> 'out) =
    fun args ->
      setup appEnv
      action args

  let Init appEnv = command "init" {
    description
      "Creates a migrondi.json file where the comand is invoked or the path provided"

    addAlias "setup"

    inputs(Init.path, Init.force, Init.merge)
    setAction(ArgumentMapper.Init appEnv >> Init.handler)
  }

  let New appEnv = command "new" {
    description
      "This will create a new SQL migration file in the configured directory for migrations"

    addAlias "create"

    inputs(SharedArguments.name None, SharedArguments.manualTransaction)
    setAction(ArgumentMapper.New appEnv >> Migrations.newMigration)
  }

  let Up appEnv = command "up" {
    description "Runs migrations against the configured database"
    addAlias "apply"

    inputs(SharedArguments.amount, SharedArguments.isDry)

    setAction(withSetup appEnv (ArgumentMapper.Up appEnv))
  }

  let Down appEnv = command "down" {
    description "Runs migrations against the configured database"
    addAlias "rollback"

    inputs(SharedArguments.amount, SharedArguments.isDry)

    setAction(withSetup appEnv (ArgumentMapper.Down appEnv))
  }

  let List appEnv = command "list" {
    description
      "Reads migrations files and the database to show what is the current state of the migrations"

    addAlias "show"

    inputs ListArgs.MigrationKind

    setAction(
      withSetup appEnv (ArgumentMapper.List appEnv >> Migrations.listMigrations)
    )
  }

  let Status appEnv = command "status" {
    description
      "Checks whether the migration file has been applied or not to the database"

    addAlias "show-state"

    inputs(SharedArguments.name(Some "Name of the migration file"))

    setAction(
      withSetup
        appEnv
        (ArgumentMapper.Status appEnv >> Migrations.migrationStatus)
    )
  }
