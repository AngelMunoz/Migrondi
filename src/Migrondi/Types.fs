namespace Migrondi.Types

exception InvalidDriverException of string
exception EmptyPath of string
exception ConfigurationExists of string

type MigrondiConfig =
    { connection: string
      migrationsDir: string
      driver: string }

type Migration =
    { id: int64
      name: string
      timestamp: int64 }

type MigrationFile =
    { name: string
      timestamp: int64
      upContent: string
      downContent: string }

[<RequireQualifiedAccess>]
type MigrationSource =
    | File of MigrationFile
    | Database of Migration

[<RequireQualifiedAccess>]
type MigrationType =
    | Up
    | Down

[<RequireQualifiedAccess>]
type Driver =
    | Mssql
    | Sqlite
    | Postgresql
    | Mysql

    static member FromString(driver: string) =
        match driver.ToLowerInvariant() with
        | "mssql" -> Mssql
        | "sqlite" -> Sqlite
        | "postgres" -> Postgresql
        | "mysql" -> Mysql
        | others ->
            let drivers = "mssql | sqlite | postgres | mysql"

            let message =
                $"""The driver selected "{others}" does not match the available drivers  {drivers}"""

            raise (InvalidDriverException message)

    static member IsValidDriver(driver: string) =
        try
            Driver.FromString driver |> ignore
            true
        with
        | _ -> false

