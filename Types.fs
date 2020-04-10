namespace Sqlator

open System

module Types =

    [<CLIMutable>]
    type SqlatorConfig =
        { connection: string
          migrationsDir: string
          driver: string }

    [<CLIMutable>]
    type Migration =
        { Id: int64
          Name: string
          Timestamp: int64 }

    type MigrationFile =
        { name: string
          timestamp: int64
          upContent: string
          downContent: string }

    [<RequireQualifiedAccess>]
    type MigrationType =
        | Up
        | Down

    type Driver =
        | Mssql
        | Sqlite

        static member FromString(driver: string) =
            match driver with
            | "MSSQL"
            | "mssql" -> Mssql
            | "SQLite"
            | "sqlite" -> Sqlite
            | others ->
                let drivers = "MSSQL | mssql | SQLite | sqlite"
                raise
                    (ArgumentException
                        (sprintf "The driver selected \"%s\" does not match the available drivers  %s" others drivers))

    [<RequireQualifiedAccess>]
    type MigrationSource =
        | File of MigrationFile
        | Database of Migration
