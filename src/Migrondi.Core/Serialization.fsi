namespace Migrondi.Core.Serialization


open System.Text
open System.Text.RegularExpressions
open System.Runtime.InteropServices

open Thoth.Json.Net

open FsToolkit.ErrorHandling

open Migrondi.Core


/// <summary>
/// This service is responsible for serializing and deserializing the configuration and migration files.
/// The default implementation uses JSON as the serialization format.
/// </summary>
[<Interface>]
type ConfigurationSerializer =

  /// <summary>
  /// Takes a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object and returns a string
  /// </summary>
  /// <param name="content">The <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object to serialize</param>
  /// <returns>A string</returns>
  abstract member Encode: content: MigrondiConfig -> string

  /// <summary>
  /// Takes a string and returns a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object
  /// </summary>
  /// <param name="content">The string to deserialize</param>
  /// <exception cref="Migrondi.Core.DeserializationFailed">
  /// Thrown when the serialization fails
  /// </exception>
  abstract member Decode: content: string -> MigrondiConfig

/// <summary>
/// This service is responsible for serializing and deserializing the migration files.
/// The default implementation coordinates between the v0.x and the v1.x formats to provide
/// backwards compatibility.
/// </summary>

[<Interface>]
type MigrationSerializer =

  /// <summary>
  /// Takes a <see cref="Migrondi.Core.Migration">Migration</see> object and returns a string
  /// </summary>
  /// <param name="content">The <see cref="Migrondi.Core.Migration">Migration</see> object to serialize</param>
  /// <returns>A string</returns>
  /// <remarks>
  /// The string is the content of the migration file
  /// </remarks>
  abstract member EncodeJson: content: Migration -> string

  /// <summary>
  /// Takes a <see cref="Migrondi.Core.Migration">Migration</see> object and returns a string
  /// </summary>
  /// <param name="content">The <see cref="Migrondi.Core.Migration">Migration</see> object to serialize</param>
  /// <returns>A string</returns>
  /// <remarks>
  /// The string is the content of the migration file
  /// </remarks>
  abstract member EncodeText: content: Migration -> string

  /// <summary>
  /// Takes a string and returns a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// </summary>
  /// <param name="content">The string to deserialize</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.Serialization.SerializationError">SerializationError</see>
  /// </returns>
  /// <remarks>
  /// The string is the content of the migration file
  /// </remarks>
  /// <exception cref="Migrondi.Core.DeserializationFailed">
  /// Thrown when the serialization fails
  /// </exception>
  abstract member DecodeJson: content: string -> Migration

  /// <summary>
  /// Takes a string and returns a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// </summary>
  /// <param name="content">The string to deserialize</param>
  /// <param name="migrationName">Optional migration name in case we're decoding v0 migrations format</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.Migration">Migration</see> object
  /// or a <see cref="Migrondi.Core.Serialization.SerializationError">SerializationError</see>
  /// </returns>
  /// <remarks>
  /// The string is the content of the migration file
  /// </remarks>
  /// <exception cref="Migrondi.Core.DeserializationFailed">
  /// Thrown when the serialization fails
  /// </exception>
  abstract member DecodeText: content: string * [<Optional>] ?migrationName: string -> Migration

/// <summary>
/// This service is responsible for serializing and deserializing the migration records.
/// The default implementation uses JSON as the serialization format.
/// </summary>
[<Interface>]
type MigrationRecordSerializer =

  /// <summary>
  /// Takes a <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> object and returns a string
  /// </summary>
  /// <param name="content">The <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> object to serialize</param>
  /// <returns>A string</returns>
  /// <remarks>
  /// The string is the content of the migration file
  /// </remarks>
  abstract member Encode: content: MigrationRecord -> string

  /// <summary>
  /// Takes a string and returns a <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> object
  /// </summary>
  /// <param name="content">The string to deserialize</param>
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> object
  /// or a <see cref="Migrondi.Core.Serialization.SerializationError">SerializationError</see>
  /// </returns>
  /// <remarks>
  /// The string is the content of the migration file
  /// </remarks>
  /// <exception cref="Migrondi.Core.DeserializationFailed">
  /// Thrown when the serialization fails
  /// </exception>
  abstract member Decode: content: string -> MigrationRecord

/// <summary>
/// This is a container service for the actual serializers of the application, given that
/// the formats don't change that often they're enclosed in this service to avoid having to
/// keep track of multiple services all over the place.
/// </summary>
[<Interface>]
type SerializerService =

  abstract member ConfigurationSerializer: ConfigurationSerializer
  abstract member MigrationSerializer: MigrationSerializer
  abstract member MigrationRecordSerializer: MigrationRecordSerializer

[<Class>]
type SerializerServiceFactory =

  /// <summary>
  /// Generates the container for the serializer services, you can provide custom implementations for the serializers.
  /// </summary>
  /// <param name="configurationSerializer">A custom configuration serializer</param>
  /// <param name="migrationRecordSerializer">A custom migration record serializer</param>
  /// <param name="migrationSerializer">A custom migration serializer</param>
  /// <returns>A serializer service</returns>
  static member GetInstance:
    ?configurationSerializer: ConfigurationSerializer *
    ?migrationRecordSerializer: MigrationRecordSerializer *
    ?migrationSerializer: MigrationSerializer ->
      SerializerService