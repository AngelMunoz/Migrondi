namespace MigrondiUI

open System
open System.IO
open Migrondi.Core


module Migrations =

  let GetMigrondi() =
    let path = Path.GetFullPath AppContext.BaseDirectory

    let config =
      Path.Combine(path, "migrondi.json")
      |> System.IO.File.ReadAllText
      |> System.Text.Json.JsonSerializer.Deserialize<MigrondiConfig>

    match config with
    | null -> ValueNone
    | config ->
      let migrondi = Migrondi.MigrondiFactory(config, path)
      migrondi.Initialize()
      ValueSome migrondi

  let Migrate(migrondi: IMigrondi) =
    let hasPending =
      migrondi.MigrationsList()
      |> Seq.exists(fun x ->
        match x with
        | Pending _ -> true
        | _ -> false)

    if hasPending then migrondi.RunUp() |> ignore else ()
