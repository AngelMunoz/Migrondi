namespace Migrondi.Core.Serialization

open System.Runtime.InteropServices

open Migrondi.Core


/// <summary>
/// This service is responsible for serializing and deserializing the migration files.
/// The default implementation coordinates between the v0.x and the v1.x formats to provide
/// backwards compatibility.
/// </summary>

[<Interface>]
type internal IMiMigrationSerializer =

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
  /// Takes a <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> object and returns a string
  /// </summary>
  /// <param name="content">The <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> object to serialize</param>
  /// <returns>A string</returns>
  /// <remarks>
  /// The string is the content of the migration file
  /// </remarks>
  abstract member EncodeMigrationRecord: content: MigrationRecord -> string

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
  abstract member DecodeMigrationRecord: content: string -> MigrationRecord

/// <summary>
/// This is a container service for the actual serializers of the application, given that
/// the formats don't change that often they're enclosed in this service to avoid having to
/// keep track of multiple services all over the place.
/// </summary>
[<Interface>]
type internal IMiConfigurationSerializer =

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

[<Class>]
type internal MigrondiSerializer =
  new: unit -> MigrondiSerializer
  interface IMiMigrationSerializer
  interface IMiConfigurationSerializer

/// <summary>
/// Provides static methods for encoding and decoding Migrondi types.
/// </summary>
[<Class; Sealed>]
type MiSerializer =

  /// <summary>
  /// Encodes a <see cref="Migrondi.Core.Migration">Migration</see> to the Migrondi text format.
  /// </summary>
  /// <param name="migration">The migration to encode.</param>
  /// <returns>A string containing the encoded migration.</returns>
  static member Encode: migration: Migration -> string

  /// <summary>
  /// Decodes Migrondi text format to a <see cref="Migrondi.Core.Migration">Migration</see>.
  /// </summary>
  /// <param name="content">The content to decode.</param>
  /// <param name="migrationName">Optional migration name, required for v0 format migrations.</param>
  /// <returns>A <see cref="Migrondi.Core.Migration">Migration</see> object.</returns>
  /// <exception cref="Migrondi.Core.DeserializationFailed">
  /// Thrown when the content cannot be decoded.
  /// </exception>
  static member Decode: content: string * [<Optional>] ?migrationName: string -> Migration

  /// <summary>
  /// Encodes a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> to JSON.
  /// </summary>
  /// <param name="config">The configuration to encode.</param>
  /// <returns>A JSON string containing the encoded configuration.</returns>
  static member Encode: config: MigrondiConfig -> string

  /// <summary>
  /// Decodes JSON to a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see>.
  /// </summary>
  /// <param name="content">The JSON content to decode.</param>
  /// <returns>A <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object.</returns>
  /// <exception cref="Migrondi.Core.DeserializationFailed">
  /// Thrown when the content cannot be decoded.
  /// </exception>
  static member DecodeConfig: content: string -> MigrondiConfig

  /// <summary>
  /// Encodes a <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> to JSON.
  /// </summary>
  /// <param name="record">The migration record to encode.</param>
  /// <returns>A JSON string containing the encoded migration record.</returns>
  static member Encode: record: MigrationRecord -> string

  /// <summary>
  /// Decodes JSON to a <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see>.
  /// </summary>
  /// <param name="content">The JSON content to decode.</param>
  /// <returns>A <see cref="Migrondi.Core.MigrationRecord">MigrationRecord</see> object.</returns>
  /// <exception cref="Migrondi.Core.DeserializationFailed">
  /// Thrown when the content cannot be decoded.
  /// </exception>
  static member DecodeRecord: content: string -> MigrationRecord