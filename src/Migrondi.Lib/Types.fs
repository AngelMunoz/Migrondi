namespace Migrondi.Types

exception InvalidDriverException of string
exception EmptyPath of string
exception ConfigurationExists of string
exception ConfigurationNotFound of string
exception InvalidMigrationName of string
exception FailedToReadFile of string
exception CommandNotParsedException of string
exception FailedToExecuteQuery of string
exception InvalidOptionSetException of string
exception AmountExeedsExistingException of string
exception MissingMigrationContent of string array

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

    member this.AsString() =
        match this with
        | Mssql -> "mssql"
        | Sqlite -> "sqlite"
        | Postgresql -> "postgres"
        | Mysql -> "mysql"

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

exception MigrationApplyFailedException of string * MigrationFile * Driver


type InitOptions =
    { path: string
      noColor: bool
      json: bool }

type NewOptions =
    { name: string
      noColor: bool
      json: bool }

type UpOptions =
    { total: int
      dryRun: bool
      noColor: bool
      json: bool }

type DownOptions =
    { total: int
      dryRun: bool
      noColor: bool
      json: bool }

type MigrationListEnum =
    | Present = 1
    | Pending = 2
    | Both = 3

type ListOptions =
    { listKind: MigrationListEnum
      amount: int
      noColor: bool
      json: bool }

type StatusOptions =
    { filename: string
      noColor: bool
      json: bool }
