namespace MigrondiUI

open System
open System.IO
open Migrondi.Core

open FsToolkit.ErrorHandling

module Migrations =

  let GetMigrondi() = voption {
    let path = Path.GetFullPath AppContext.BaseDirectory

    let migrations = Path.Combine(path, "migrations")
    let dataSource = Path.Combine(path, "migrondi.db")

    let config = {
      MigrondiConfig.Default with
          migrations = migrations
          connection = $"Data Source={dataSource};"
    }

    let migrondi = Migrondi.MigrondiFactory(config, path)
    migrondi.Initialize()

    return migrondi
  }

  let Migrate(migrondi: IMigrondi) =
    let hasPending =
      migrondi.MigrationsList()
      |> Seq.exists(fun x ->
        match x with
        | Pending _ -> true
        | _ -> false)

    if hasPending then migrondi.RunUp() |> ignore else ()
