namespace Migrondi.Core

open System
open FsToolkit.ErrorHandling
open System.Runtime.CompilerServices

[<AutoOpen>]
module internal Matcher =

  module internal V0 =
    /// Pre-v1 version of migrondi used this schema for your migrations names
    /// This is currently deprecated, don't use this to validate your names.
    /// Please check out the V1 module for the current schema
    [<Literal>]
    val MigrationNameSchema: string = "^(?<Name>.+)_(?<Timestamp>[0-9]+).(sql|SQL)$"

    val reg: Lazy<System.Text.RegularExpressions.Regex>

  module internal V1 =
    /// This is only supported filename format at the moment, your implementations
    /// have to have this REGEX in mind when creating migration in custom implementations
    /// of the  <see cref="Migrondi.Core.FileSystem.IMiFileSystem">FileSystem</see> type
    [<Literal>]
    val MigrationNameSchema: string = "^(?<Timestamp>[0-9]+)_(?<Name>.+).(sql|SQL)$"

    val reg: Lazy<System.Text.RegularExpressions.Regex>


  val (|V0Name|V1Name|NotMatched|): string -> Choice<string * int64, string * int64, unit>

/// DU that represents the currently supported drivers
[<RequireQualifiedAccess>]
type MigrondiDriver =
  | Mssql
  | Sqlite
  | Postgresql
  | Mysql

  /// Returns a string representation of the driver
  member AsString: string

  /// <summary>Takes a string and tries to convert it to a MigrondiDriver</summary>
  /// <param name="value">The string to convert</param>
  /// <returns> A MigrondiDriver if the conversion was successful</returns>
  /// <exception cref="System.ArgumentException">
  /// Thrown when the conversion was not successful
  /// </exception>
  /// <remarks>
  /// if the string is not a valid driver then it will throw an exception
  /// with the name of the driver that was not found
  /// </remarks>
  static member FromString: value: string -> MigrondiDriver

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
    manualTransaction: bool
  }

  /// <summary>
  /// Takes a filename and tries to extract the migration information from it
  /// </summary>
  /// <param name="filename">The filename to extract the migration information from</param>
  /// <returns>
  /// A Result that may contain a tuple of the migration name and timestamp
  /// or a set of strings that may represent all of the found errors while validating the filename
  /// </returns>
  static member ExtractFromFilename: filename: string -> Validation<string * int64, string>

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
  static member ExtractFromPath: path: string -> Validation<string * int64, string>

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

  member internal AsString: string

/// <summary>
/// Used by the Migrondi Service to represent the status of a migration.
/// </summary>
type MigrationStatus =
  | Applied of Migration
  | Pending of Migration

  member internal Value: Migration

[<AutoOpen>]
module Exceptions =
  val internal reriseCustom: exn: exn -> 'ReturnValue

/// <summary>
/// Represents an error that happened while we were trying to create the database
/// and add the miogrations table within.
/// </summary>
/// <remarks>
/// This is not thrown if the migration's table has already been created.
/// </remarks>
exception SetupDatabaseFailed

/// <summary>
/// Represents an error that happened while we were trying to apply a migration.
/// </summary>
/// <remarks>
/// Before throwing this exception the transaction that encloses the migration
/// will be rolled back.
/// </remarks>
exception MigrationApplicationFailed of Migration: Migration

/// <summary>
/// Represents an error that happened while we were trying to revert a migration.
/// </summary>
/// <remarks>
/// Before throwing this exception the transaction that encloses the migration
/// will be rolled back.
/// </remarks>
exception MigrationRollbackFailed of Migration: Migration

/// <summary>
/// This exception is thrown when the FileSystem is unable to read a migration source,
/// configuration file or any other source that requires interaction with the file system.
/// </summary>
exception SourceNotFound of path: string * name: string

/// <summary>
/// This exception is thrown when the FileSystem is unable to deserialize a migration source,
/// configuration file or any other source that requires deserialization.
/// </summary>
exception DeserializationFailed of Content: string * Reason: string

/// <summary>
/// This exception is thrown when the content cannot be deserialized from source.
/// This tipically happens if required fields are removed from the source.
/// </summary>
exception MalformedSource of SourceName: string * Content: string * Reason: string


[<Extension; Class>]
type ExceptionExtensions =
  [<Extension>]
  static member inline Value: MalformedSource -> string * string * string

  [<Extension>]
  static member inline Value: DeserializationFailed -> string * string