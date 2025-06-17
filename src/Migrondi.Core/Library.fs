namespace Migrondi.Core

open System
open FsToolkit.ErrorHandling
open System.Runtime.CompilerServices

[<AutoOpen>]
module internal Matcher =
  open System.Text.RegularExpressions

  module V0 =
    [<Literal>]
    let MigrationNameSchema: string =
      "^(?<Name>.+)_(?<Timestamp>[0-9]+).(sql|SQL)$"

    let reg = lazy Regex(MigrationNameSchema)

  module V1 =

    [<Literal>]
    let MigrationNameSchema: string =
      "^(?<Timestamp>[0-9]+)_(?<Name>.+).(sql|SQL)$"

    let reg = lazy Regex(MigrationNameSchema)

  [<return: Struct>]
  let (|HasGroup|_|) (name: string) (groups: Match) =
    if not groups.Success then
      ValueNone
    else
      match groups.Groups[name] with
      | null -> ValueNone
      | group -> ValueSome group.Value

  let (|V0Name|V1Name|NotMatched|) (filename: string) =
    match V0.reg.Value.Match filename with
    | HasGroup "Name" name & HasGroup "Timestamp" timestamp ->
      match Int64.TryParse timestamp with
      | true, timestamp -> V0Name(name, timestamp)
      | _ -> NotMatched
    | _ ->
      match V1.reg.Value.Match filename with
      | HasGroup "Name" name & HasGroup "Timestamp" timestamp ->
        match Int64.TryParse timestamp with
        | true, timestamp -> V1Name(name, timestamp)
        | _ -> NotMatched
      | _ -> NotMatched


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



type MigrondiConfig = {
  connection: string
  migrations: string
  tableName: string
  driver: MigrondiDriver
} with

  static member Default: MigrondiConfig = {
    connection = "Data Source=./migrondi.db"
    migrations = "./migrations"
    tableName = "__migrondi_migrations"
    driver = MigrondiDriver.Sqlite
  }

type MigrationRecord = {
  id: int64
  name: string
  timestamp: int64
}

type Migration = {
  name: string
  timestamp: int64
  upContent: string
  downContent: string
  manualTransaction: bool
} with

  static member internal ExtractFromFilename
    (filename: string)
    : Validation<string * int64, string> =
    match filename with
    | V0Name(name, timestamp)
    | V1Name(name, timestamp) -> Ok(name, timestamp)
    | null
    | NotMatched -> Error [ "Invalid migration name" ]

  static member internal ExtractFromPath
    (path: string)
    : Validation<string * int64, string> =
    Migration.ExtractFromFilename(path |> System.IO.Path.GetFileName)


[<RequireQualifiedAccess>]
type MigrationSource =
  | SourceCode of Migration
  | Database of MigrationRecord

[<RequireQualifiedAccess>]
type MigrationType =
  | Up
  | Down

  member internal this.AsString =
    match this with
    | Up -> "UP"
    | Down -> "DOWN"


type MigrationStatus =
  | Applied of Migration
  | Pending of Migration

  member internal this.Value =
    match this with
    | Applied migration
    | Pending migration -> migration

[<AutoOpen>]
module Exceptions =
  open System.Runtime.ExceptionServices

  let internal reriseCustom<'ReturnValue> (exn: exn) =
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