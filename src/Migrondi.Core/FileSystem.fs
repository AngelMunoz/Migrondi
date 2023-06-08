namespace Migrondi.Core.FileSystem

open FSharp.UMX

module Units =

  [<Measure>]
  type UserFilePath

  [<Measure>]
  type UserDirectoryPath

open Units
open Migrondi.Core
open Migrondi.Core.Serialization

[<Struct>]
type WriteFileError =
  | PathNotFound of targetPath: string
  | FileAlreadyExists of filepath: string * filename: string
  | UnableToWriteFile of reason: string
  | MalformedContent of validityReason: string

[<Struct>]
type ReadFileError =
  | PathNotFound of path: string
  | FileNotFound of filepath: string * filename: string
  | Malformedfile of validityReason: string

type FileSystemEnv =

  /// <summary>
  /// Represents the root of the project where the migrondi.json file is located.
  /// </summary>
  /// <remarks>
  /// For file system/web request operations this should be an absolute path to the migrondi.json file
  /// </remarks>
  abstract member RootPath: string<UserDirectoryPath>


  /// <summary>
  /// Take the path to a configuration file, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="serializer">A serializer that will be used to decode the string content from the configuration</param>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <returns>A <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object</returns>
  abstract member ReadConfiguration:
    serializer: #SerializerEnv * readFrom: string<UserFilePath> ->
      Result<MigrondiConfig, ReadFileError>

  /// <summary>
  /// Take the path to a configuration file, reads and transforms it into a configuration object
  /// </summary>
  /// <param name="serializer">A serializer that will be used to decode the string content from the configuration</param>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the configuration file</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ReadConfigurationAsync:
    serializer: #SerializerEnv * readFrom: string<UserFilePath> ->
      Async<Result<MigrondiConfig, ReadFileError>>

  /// <summary>
  /// Take a configuration object and writes it to a file
  /// </summary>
  /// <param name="config">The configuration object</param>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="writeTo">The path to the configuration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member WriteConfiguration:
    config: MigrondiConfig *
    serializer: #SerializerEnv *
    writeTo: string<UserFilePath> ->
      Result<unit, WriteFileError>

  /// <summary>
  /// Take a configuration object and writes it to a file
  /// </summary>
  /// <param name="config">The configuration object</param>
  /// <param name="serializer">A serializer that will be used to encode configuration into a string</param>
  /// <param name="writeTo">The path to the configuration file</param>
  /// <returns>
  /// A unit result object that means that the operation was successful
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member WriteConfigurationAsync:
    config: MigrondiConfig *
    serializer: #SerializerEnv *
    writeTo: string<UserFilePath> ->
      Async<Result<unit, WriteFileError>>

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ReadMigration:
    readFrom: string<UserFilePath> -> Result<Migration, ReadFileError>

  /// <summary>
  /// Takes a path to a migration, reads its contents and transforms it into a Migration object
  /// </summary>
  /// <param name="readFrom">A path Relative to the RootPath that targets to the migration</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.FileSystem.ReadFileError">ReadFileError</see>
  /// </returns>
  abstract member ReadMigrationAsync:
    string<UserFilePath> -> Async<Result<Migration, ReadFileError>>

  abstract member GetMigrations:
    string<UserDirectoryPath> -> Result<Migration seq, ReadFileError>

  abstract member GetMigrationsAsync:
    string<UserDirectoryPath> -> Async<Result<Migration seq, ReadFileError>>

  abstract member WriteMigration: string<UserFilePath> * Migration -> unit

  abstract member WriteMigrationAsync:
    string<UserFilePath> * Migration -> Async<unit>