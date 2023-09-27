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
type MigrondiConfig =
  {
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

  static member Default: MigrondiConfig

/// <summary>
/// Represents a migration stored in the database.
/// The contents of the queries are not stored here as the purpose of this object is to
/// be able to identify which migrations have been applied to the database.
/// </summary>
type MigrationRecord =
  { id: int64
    name: string
    timestamp: int64 }

/// <summary>
/// Object that represents an SQL migration file on disk, and are often used provide more context
/// or information while logging information about migrations.
/// </summary>
type Migration =
  {
    name: string
    timestamp: int64
    /// the actual SQL statements that will be used to run against the database
    upContent: string
    /// the actual SQL statements that will be used to run against the database
    /// when rolling back migrations from the database
    downContent: string
  }

  /// <summary>
  /// Takes a filename and tries to extract the migration information from it
  /// </summary>
  /// <param name="filename">The filename to extract the migration information from</param>
  /// <returns>
  /// A Result that may contain a tuple of the migration name and timestamp
  /// or a set of strings that may represent all of the found errors while validating the filename
  /// </returns>
  static member ExtractFromFilename: filename: string -> Validation<(string * int64), string>

  /// <summary>
  /// Takes a path and tries to extract the migration information from it
  /// </summary>
  /// <param name="path">The path to extract the migration information from</param>
  /// <remarks>
  /// This is mostly an utility function as internally calls `ExtractFromFilename` with System.IO's `Path.GetFileName`
  /// </remarks>
  /// <returns>
  /// A Result that may contain a tuple of the migration name and timestamp
  /// or a set of strings that may represent all of the found errors while validating the path
  /// </returns>
  static member ExtractFromPath: path: string -> Validation<(string * int64), string>

/// Migration information can be obtained from a file or the database
/// this DU allows to identify where the information is coming from
[<RequireQualifiedAccess>]
type MigrationSource =
  | SourceCode of Migration
  | Database of MigrationRecord

/// <summary>
/// Represents the desired action direction for a migration
/// Up meaning that the migration will be applied to the database
/// Down meaning that the migration will be rolled back from the database
/// </summary>
[<RequireQualifiedAccess>]
type MigrationType =
  | Up
  | Down

  member AsString: string

type MigrationStatus =
  | Applied of Migration
  | Pending of Migration

  member Value: Migration

[<AutoOpen>]
module Exceptions =
  open System.Runtime.ExceptionServices
  val inline reriseCustom: exn: exn -> 'ReturnValue

exception SetupDatabaseFailed
exception MigrationApplicationFailed of Migration: Migration
exception MigrationRollbackFailed of Migration: Migration
exception SourceNotFound of path: string * name: string
exception DeserializationFailed of Content: string * Reason: string
exception MalformedSource of SourceName: string * Content: string * Reason: string

type MalformedSource with

  member Value: string * string * string

type DeserializationFailed with

  member Value: string * string