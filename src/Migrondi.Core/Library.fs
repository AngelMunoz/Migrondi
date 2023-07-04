namespace Migrondi.Core

open FsToolkit.ErrorHandling

/// DU that represents the currently supported drivers
[<RequireQualifiedAccess>]
type MigrondiDriver =
  | Mssql
  | Sqlite
  | Postgresql
  | Mysql

/// Represents the configuration that will be used to run migrations
type MigrondiConfig = {
  /// An ADO compatible connection string
  /// which will be used to connect to the database
  connection: string
  /// A relative path like string to the directory where migration files are stored
  migrations: string
  /// The name of the table that will be used to store migration information
  tableName: string
  /// a string that represents the drivers that can be used
  /// mysql | postgres | mssql | sqlite
  driver: MigrondiDriver
}

/// Represents a migration stored in the database
type MigrationRecord = {
  id: int64
  name: string
  timestamp: int64
}

/// Object that represents an SQL migration file on disk
type Migration = {
  name: string
  timestamp: int64
  /// the actual SQL statements that will be used to run against the database
  upContent: string
  /// the actual SQL statements that will be used to run against the database
  /// when rolling back migrations from the database
  downContent: string
} with

  static member ExtractFromFilename
    (filename: string)
    : Validation<string * int64, string> =
    validation {
      let nameSchema =
        System.Text.RegularExpressions.Regex(
          "^(?<Name>.+)_(?<Timestamp>[0-9]+).(sql|SQL)$"
        )

      let value = nameSchema.Match filename

      let name = value.Groups["Name"].Value

      match name |> Option.ofObj with
      | None -> return! Error "Invalid migration name"
      | Some _ -> ()

      let timestamp =
        try
          value.Groups["Timestamp"].Value |> int64 |> Ok
        with ex ->
          Error ex.Message

      match timestamp with
      | Ok timestamp -> return name, timestamp
      | Error error -> return! Error error
    }

  static member ExtractFromPath
    (path: string)
    : Validation<string * int64, string> =
    Migration.ExtractFromFilename(path |> System.IO.Path.GetFileName)


/// Migration information can be obtained from a file or the database
/// this DU allows to identify where the information is coming from
[<RequireQualifiedAccess>]
type MigrationSource =
  | SourceCode of Migration
  | Database of MigrationRecord

[<RequireQualifiedAccess>]
type MigrationType =
  | Up
  | Down

  member this.AsString =
    match this with
    | Up -> "UP"
    | Down -> "DOWN"


[<Struct>]
type SerializationError =
  | MalformedContent of content: string * reason: string

  member this.Value =
    match this with
    | MalformedContent(content, reason) -> content, reason

  member this.Content =
    match this with
    | MalformedContent(content, _) -> content

  member this.Reason =
    match this with
    | MalformedContent(_, reason) -> reason


type MigrationStatus =
  | Applied of Migration
  | Pending of Migration

  member this.Value =
    match this with
    | Applied migration
    | Pending migration -> migration

[<AutoOpen>]
module Exceptions =
  open System.Runtime.ExceptionServices

  let inline reriseCustom<'ReturnValue> (exn: exn) =
    ExceptionDispatchInfo.Capture(exn).Throw()
    Unchecked.defaultof<'ReturnValue>


exception SetupDatabaseFailed
exception MigrationApplicationFailed of migration: Migration
exception MigrationRollbackFailed of migration: Migration

exception SourceNotFound of path: string * name: string

exception MalformedSource of
  sourceName: string *
  serializationError: SerializationError