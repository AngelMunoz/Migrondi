namespace Migrondi.Core.FileSystem

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open System.Runtime.InteropServices

open Microsoft.Extensions.Logging

open Migrondi.Core
open Migrondi.Core.Serialization

/// <summary>
/// Minimal abstraction for reading/writing raw migration content.
/// Users implement this to provide custom sources (HTTP, S3, Azure Blob, etc.).
/// The library handles all serialization/deserialization internally.
/// </summary>
[<Interface>]
type IMiMigrationSource =

  /// <summary>
  /// Reads raw string content from a URI
  /// </summary>
  /// <param name="uri">Full URI to the resource (file://, http://, s3://, etc.)</param>
  /// <returns>The raw content as a string</returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the resource is not found
  /// </exception>
  abstract member ReadContent: uri: Uri -> string

  /// <summary>
  /// Reads raw string content from a URI
  /// </summary>
  /// <param name="uri">Full URI to the resource (file://, http://, s3://, etc.)</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>The raw content as a string</returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the resource is not found
  /// </exception>
  abstract member ReadContentAsync:
    uri: Uri * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<string>

  /// <summary>
  /// Writes raw string content to a URI
  /// </summary>
  /// <param name="uri">Full URI to the resource</param>
  /// <param name="content">The raw content to write</param>
  abstract member WriteContent: uri: Uri * content: string -> unit

  /// <summary>
  /// Writes raw string content to a URI
  /// </summary>
  /// <param name="uri">Full URI to the resource</param>
  /// <param name="content">The raw content to write</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  abstract member WriteContentAsync:
    uri: Uri *
    content: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  /// <summary>
  /// Lists migration URIs at a location
  /// </summary>
  /// <param name="locationUri">Full URI to the directory-like location</param>
  /// <returns>A sequence of URIs to migration files</returns>
  abstract member ListFiles: locationUri: Uri -> Uri seq

  /// <summary>
  /// Lists migration URIs at a location
  /// </summary>
  /// <param name="locationUri">Full URI to the directory-like location</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>A sequence of URIs to migration files</returns>
  abstract member ListFilesAsync:
    locationUri: Uri * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Uri seq>


/// <summary>
/// This is an abstraction to the file system, it allows for custom implementations to either use
/// a physical file system or even a virtual one.
/// It provides both sync and async methods to read and write migrondi specific files.
/// </summary>
[<Interface>]
type internal IMiFileSystem =

  /// <summary>
  /// Take the path to a configuration source, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <returns>A <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object</returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the source is not found
  /// </exception>
  /// <exception cref="Migrondi.Core.FileSystem.MalformedSource">
  /// Thrown when the source is found but can't be deserialized from disk
  /// </exception>
  abstract member ReadConfiguration: readFrom: string -> MigrondiConfig

  /// <summary>
  /// Take the path to a configuration source, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the file is not found
  /// </exception>
  /// <exception cref="Migrondi.Core.FileSystem.MalformedSource">
  /// Thrown when the file is found but can't be deserialized from the source
  /// </exception>
  abstract member ReadConfigurationAsync:
    readFrom: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<MigrondiConfig>

  /// <summary>
  /// Take a configuration object and writes it to a location dictated by the `writeTo` parameter
  /// </summary>
  /// <param name="config">The configuration object</param>
  /// <param name="writeTo">The path to the configuration file</param>
  abstract member WriteConfiguration:
    config: MigrondiConfig * writeTo: string -> unit

  /// <summary>
  /// Take a configuration object and writes it to a file
  /// </summary>
  /// <param name="config">The configuration object</param>
  /// <param name="writeTo">The path to the configuration file</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member WriteConfigurationAsync:
    config: MigrondiConfig *
    writeTo: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="migrationName">A path Relative to the RootPath that targets to the migration</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  /// Thrown when the file is not found
  /// </exception>
  /// <exception cref="Migrondi.Core.FileSystem.MalformedSource">
  /// Thrown when the file is found but can't be deserialized from the source
  /// </exception>
  abstract member ReadMigration: migrationName: string -> Migration

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="migrationName">A path Relative to the RootPath that targets to the migration</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="Migrondi.Core.FileSystem.SourceNotFound">
  ///  Thrown when the file is not found
  /// </exception>
  abstract member ReadMigrationAsync:
    migrationName: string * [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration>

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="migration">The migration object</param>
  /// <param name="migrationName">The path to the migration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigration:
    migration: Migration * migrationName: string -> unit

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="migration">The migration object</param>
  /// <param name="migrationName">The path to the migration file</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigrationAsync:
    migration: Migration *
    migrationName: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="migrationsLocation">A path Relative to the RootPath that targets to the migration.</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  /// <exception cref="System.AggregateException">
  /// A list of exceptions in case the sources were not readable or malformed
  /// This normally includes exceptions of the type <see cref="Migrondi.Core.FileSystem.MalformedSource">MalformedSource</see>
  /// </exception>
  abstract member ListMigrations:
    migrationsLocation: string -> Migration IReadOnlyList

  /// <summary>
  /// Takes a path to a directory-like source, and reads the sql scripts inside it
  /// </summary>
  /// <param name="migrationsLocation">A path Relative to the RootPath that targets to the migration</param>
  /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ListMigrationsAsync:
    migrationsLocation: string *
    [<Optional>] ?cancellationToken: CancellationToken ->
      Task<Migration IReadOnlyList>


[<Class>]
type internal MiFileSystem =
  /// <summary>
  /// Generates a new file system service from a migration source (HTTP, S3, etc.)
  /// </summary>
  /// <param name="logger">A logger instance</param>
  /// <param name="configurationSerializer">A configuration serializer</param>
  /// <param name="migrationSerializer">A migration serializer</param>
  /// <param name="projectRootUri">An absolute Uri to the project root.</param>
  /// <param name="migrationsRootUri">A relative Uri to the migrations root.</param>
  /// <param name="source">An optional migration source implementation (HTTP, S3, etc.). If not provided, defaults to physical file system.</param>
  /// <returns>A new file system service</returns>
  new:
    logger: ILogger *
    configurationSerializer: IMiConfigurationSerializer *
    migrationSerializer: IMiMigrationSerializer *
    projectRootUri: Uri *
    migrationsRootUri: Uri *
    [<Optional>] ?source: IMiMigrationSource ->
      MiFileSystem

  interface IMiFileSystem
