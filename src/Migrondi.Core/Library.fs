﻿namespace Migrondi.Core

open FsToolkit.ErrorHandling
open System.Runtime.CompilerServices

[<AutoOpen>]
module Core =
  [<Literal>]
  let MigrationNameSchema = "^(?<Name>.+)_(?<Timestamp>[0-9]+).(sql|SQL)$"

/// DU that represents the currently supported drivers
[<RequireQualifiedAccess>]
type MigrondiDriver =
  | Mssql
  | Sqlite
  | Postgresql
  | Mysql

  member this.AsString =
    match this with
    | Mssql -> "mssql"
    | Sqlite -> "sqlite"
    | Postgresql -> "postgresql"
    | Mysql -> "mysql"

  static member FromString(value: string) =

    match value.ToLowerInvariant() with
    | "mssql" -> Mssql
    | "sqlite" -> Sqlite
    | "postgresql" -> Postgresql
    | "mysql" -> Mysql
    | _ -> invalidArg (nameof value) $"Unknown driver: %s{value}"



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
} with

  static member Default: MigrondiConfig = {
    connection = "Data Source=./migrondi.db"
    migrations = "./migrations"
    tableName = "__migrondi_migrations"
    driver = MigrondiDriver.Sqlite
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

  manualTransaction: bool
} with

  static member ExtractFromFilename
    (filename: string)
    : Validation<string * int64, string> =
    validation {
      let nameSchema = System.Text.RegularExpressions.Regex(MigrationNameSchema)

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
exception MigrationApplicationFailed of Migration: Migration
exception MigrationRollbackFailed of Migration: Migration

exception SourceNotFound of path: string * name: string

exception DeserializationFailed of Content: string * Reason: string

exception MalformedSource of
  SourceName: string *
  Content: string *
  Reason: string


[<Extension; Class>]
type ExceptionExtensions =

  [<Extension>]
  static member inline Value(this: MalformedSource) =
    this.SourceName, this.Content, this.Reason

  [<Extension>]
  static member inline Value(this: DeserializationFailed) =
    this.Content, this.Reason

// ensure extensions are visible to VB
[<assembly: Extension>]
do ()