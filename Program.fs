open System
open FSharp.Data.Dapper
open Sqlator.Migrations

    [<EntryPoint>]
    let main argv =
        OptionHandler.RegisterTypes()
        let results = asyncRunMigrations()
        printfn "%A" results
        0 // return an integer exit code
