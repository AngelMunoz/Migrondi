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
/// This is an abstraction to the file system, it allows for custom implementations to either use
/// a physical file system or even a virtual one.
/// It provides both sync and async methods to read and write migrondi specific files.
/// </summary>
[<Interface>]
type FileSystemService =

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
    readFrom: string * [<Optional>] ?cancellationToken: CancellationToken -> Task<MigrondiConfig>

  /// <summary>
  /// Take a configuration object and writes it to a location dictated by the `writeTo` parameter
  /// </summary>
  /// <param name="config">The configuration object</param>
  /// <param name="writeTo">The path to the configuration file</param>
  abstract member WriteConfiguration: config: MigrondiConfig * writeTo: string -> unit

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
    config: MigrondiConfig * writeTo: string * [<Optional>] ?cancellationToken: CancellationToken -> Task

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
    migrationName: string * [<Optional>] ?cancellationToken: CancellationToken -> Task<Migration>

  /// <summary>
  /// Takes a migration and serializes its contents into a location dictated by the path
  /// </summary>
  /// <param name="migration">The migration object</param>
  /// <param name="migrationName">The path to the migration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// </returns>
  abstract member WriteMigration: migration: Migration * migrationName: string -> unit

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
    migration: Migration * migrationName: string * [<Optional>] ?cancellationToken: CancellationToken -> Task

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
  abstract member ListMigrations: migrationsLocation: string -> Migration IReadOnlyList

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
    migrationsLocation: string * [<Optional>] ?cancellationToken: CancellationToken -> Task<Migration IReadOnlyList>


[<Class>]
type FileSystemServiceFactory =
  /// <summary>
  /// Generates a new file system service, this can be further customized by passing in a custom serializer
  /// an absolute Uri to the project root and a relative Uri to the migrations root.
  /// </summary>
  /// <param name="projectRootUri">An absolute Uri to the project root.</param>
  /// <param name="migrationsRootUri">A relative Uri to the migrations root.</param>
  /// <param name="logger">A logger instance</param>
  /// <param name="configurationSerializer">A custom configuration serializer</param>
  /// <param name="migrationSerializer">A custom migration serializer</param>
  /// <returns>A new file system service</returns>
  static member GetInstance:
    projectRootUri: Uri *
    migrationsRootUri: Uri *
    logger: ILogger *
    [<Optional>] ?configurationSerializer: ConfigurationSerializer *
    [<Optional>] ?migrationSerializer: MigrationSerializer ->
      FileSystemService