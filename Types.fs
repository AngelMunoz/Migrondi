namespace Sqlator

open System
open RepoDb.Attributes

module Types =

    [<CLIMutable>]
    type SqlatorConfig =
        { connection: string
          migrationsDir: string
          driver: string }

    [<CLIMutable>]
    [<Map("migration")>]
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
                raise
                    (ArgumentException
                        (sprintf "The driver selected \"%s\" does not match the available drivers  %s" others drivers))

    [<RequireQualifiedAccess>]
    type MigrationSource =
        | File of MigrationFile
        | Database of Migration
