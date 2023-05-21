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
    { /// An ADO compatible connection string
      /// which will be used to connect to the database
      connection: string
      /// A relative path like string to the directory where migration files are stored
      migrationsDir: string
      /// a string that represents the drivers that can be used
      /// mysql | postgres | mssql | sqlite
      driver: string }

type Migration =
    { id: int64
      name: string
      timestamp: int64 }

/// Object that represents an SQL migration file on disk
type MigrationFile =
    { name: string
      timestamp: int64
      /// the actual SQL statements that will be used to run against the database
      upContent: string
      /// the actual SQL statements that will be used to run against the database
      /// when rolling back migrations from the database
      downContent: string }

/// Migration information can be obtained from a file or the database
/// this DU allows to identify where the information is coming from
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

/// Used when initializing a migrondi workspace directory
type InitOptions =
    { /// The path where to create a configuration file and a child migrations directory
      path: string
      noColor: bool
      json: bool }

/// Used when creating a new migration file
type NewOptions =
    { // A string that contains the name of the migration file, this name cannot include underscores "_"
      name: string
      noColor: bool
      /// output each line of the standard Output as a JSON object
      json: bool }

/// Used when you need to run migrations against the database
type UpOptions =
    { /// amount of migrations to run, use 0 to run them all or a positive integer to run a specific amount
      /// if the amount exceeds the existing available migrations
      /// it will default to the amount of available existing migrations
      total: int
      /// Runs a "Fake" set of migrations, useful to confirm that the operation
      /// will run as you intend (order and content)
      dryRun: bool
      noColor: bool
      /// output each line of the standard Output as a JSON object
      json: bool }

type DownOptions =
    { /// amount of migrations to run, use 0 to run them all or a positive integer to run a specific amount
      /// if the amount exceeds the existing available migrations
      /// it will default to the amount of available existing migrations
      total: int
      /// Runs a "Fake" set of migrations, useful to confirm that the operation
      /// will run as you intend (order and content)
      dryRun: bool
      noColor: bool
      /// output each line of the standard Output as a JSON object
      json: bool }

type MigrationListEnum =
    | Present = 1
    | Pending = 2
    | Both = 3

type ListOptions =
    { listKind: MigrationListEnum
      /// amount of migrations to list, defaults to 5, use 0 to bring all of them
      amount: int
      noColor: bool
      /// output each line of the standard Output as a JSON object
      json: bool }

type StatusOptions =
    { filename: string
      noColor: bool
      /// output each line of the standard Output as a JSON object
      json: bool }
