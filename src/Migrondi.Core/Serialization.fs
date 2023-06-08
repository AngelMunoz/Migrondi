namespace Migrondi.Core.Serialization


open System
open System.IO
open System.Text
open System.Text.RegularExpressions

open Thoth.Json.Net

open FsToolkit.ErrorHandling

open Migrondi.Core

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
  /// <returns>
  /// A Result that may contain a <see cref="Migrondi.Core.MigrondiConfig">MigrondiConfig</see> object
  /// or a <see cref="Migrondi.Core.Serialization.SerializationError">SerializationError</see>
  /// </returns>
  abstract member Decode:
    content: string -> Result<MigrondiConfig, SerializationError>

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
  abstract member DecodeJson:
    content: string -> Result<Migration, SerializationError>

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
  abstract member DecodeText:
    content: string -> Result<Migration, SerializationError>

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
  abstract member Decode:
    content: string -> Result<MigrationRecord, SerializationError>

[<RequireQualifiedAccess;
  CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module MigrondiDriver =
  let Encode: Encoder<MigrondiDriver> =
    fun (driver: MigrondiDriver) ->
      let name =
        match driver with
        | MigrondiDriver.Mssql -> "mssql"
        | MigrondiDriver.Sqlite -> "sqlite"
        | MigrondiDriver.Postgresql -> "postgres"
        | MigrondiDriver.Mysql -> "mysql"

      Encode.string name

  let Decode: Decoder<MigrondiDriver> =
    Decode.string
    |> Decode.andThen(fun value ->
      match value.ToLowerInvariant() with
      | "sqlserver"
      | "mssql" -> Decode.succeed MigrondiDriver.Mssql
      | "sqlite" -> Decode.succeed MigrondiDriver.Sqlite
      | "postgressql"
      | "postgres" -> Decode.succeed MigrondiDriver.Postgresql
      | "mariadb"
      | "mysql" -> Decode.succeed MigrondiDriver.Mysql
      | name -> Decode.fail $"Invalid driver name: '{name}'"
    )

[<RequireQualifiedAccess;
  CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module MigrondiConfig =

  let Encode: Encoder<MigrondiConfig> =
    fun (config: MigrondiConfig) ->
      Encode.object [
        "connection", Encode.string config.connection
        "migrations", Encode.string config.migrations
        "driver", MigrondiDriver.Encode config.driver
      ]

  let Decode: Decoder<MigrondiConfig> =
    Decode.object(fun get -> {
      connection = get.Required.Field "connection" Decode.string
      migrations = get.Required.Field "migrations" Decode.string
      driver = get.Required.Field "driver" MigrondiDriver.Decode
      tableName =
        get.Optional.Field "tableName" Decode.string
        |> Option.defaultValue "__migrondi_migrations"
    })

[<RequireQualifiedAccess;
  CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]

module Migration =
  let migrationDelimiter (key: string, value: string option) =
    let value =
      match value with
      | Some value -> $"={value}"
      | None -> ""

    $"-- ---------- MIGRONDI:%s{key}{value} ----------"

  let EncodeJson: Encoder<Migration> =
    fun (migration: Migration) ->
      Encode.object [
        "name", Encode.string migration.name
        "timestamp", Encode.int64 migration.timestamp
        "upContent", Encode.string migration.upContent
        "downContent", Encode.string migration.downContent
      ]

  let EncodeText (migration: Migration) : string =
    // GOAL: Migrations Format V1 have to be encoded like this:
    // -- Do not remove MIGRONDI comments.
    // -- MIGRONDI:Name=AddUsersTable
    // -- MIGRONDI:TIMESTAMP=1586550686936
    // -- ---------- MIGRONDI:UP ----------
    // -- Write your Up migrations here
    //
    // -- ---------- MIGRONDI:UP ----------
    // -- Write how to revert the migration here
    //

    let sb = StringBuilder()
    let name = migrationDelimiter("NAME", Some migration.name)

    let timestamp =
      migrationDelimiter("TIMESTAMP", Some(migration.timestamp.ToString()))

    let up = migrationDelimiter(MigrationType.Up.AsString, None)
    let down = migrationDelimiter(MigrationType.Down.AsString, None)

    let content =
      // for new migrations the "write up your migrations here" should be provided
      // by us the same applies to the down migration
      // but it always has to include the up and down delimiter
      // Note: we may add metadata in the future above the up delimiter
      sb
        .AppendLine(name)
        .AppendLine(timestamp)
        .AppendLine(up)
        .AppendLine(migration.upContent)
        .AppendLine(down)
        .AppendLine(migration.downContent)
        .ToString()

    content

  let DecodeTextV0
    (content: string)
    (name: string)
    : Result<Migration, string> =
    result {
      // Migrations Format V0 are already encoded like this:
      //-- ---------- MIGRONDI:UP:1586550686936 --------------
      //-- Write your Up migrations here
      //
      //-- ---------- MIGRONDI:DOWN:1586550686936 --------------
      //-- Write how to revert the migration here

      let matcher =
        new Regex(
          "-- ---------- MIGRONDI:(?<Identifier>UP|DOWN):(?<Timestamp>[0-9]+) ----------",
          RegexOptions.Multiline
        )

      let collection = matcher.Matches(content)

      do!
        collection.Count = 2
        |> Result.requireTrue "Invalid Migrations Format"
        |> Result.ignore

      let upIndex =
        collection
        |> Seq.find(fun value -> value.Groups["Identifier"].Value = "UP")
        |> fun value -> value.Index

      let downIndex =
        collection
        |> Seq.find(fun value -> value.Groups["Identifier"].Value = "DOWN")
        |> fun value -> value.Index

      let! timestamp =
        try
          collection[0].Groups["Timestamp"].Value |> int64 |> Ok
        with ex ->
          Error $"Invalid timestamp: {ex.Message}"

      let! upContent =
        try
          content[upIndex..downIndex] |> Ok
        with ex ->
          Error $"Invalid up content: {ex.Message}"

      let! downContent =
        try
          content[downIndex..] |> Ok
        with ex ->
          Error $"Invalid down content: {ex.Message}"

      return {
        name = name
        upContent = upContent
        downContent = downContent
        timestamp = timestamp
      }
    }


  let DecodeText (content: string) : Result<Migration, string> =
    raise(NotImplementedException())

  let DecodeJson: Decoder<Migration> =
    Decode.object(fun get -> {
      name = get.Required.Field "name" Decode.string
      upContent = get.Required.Field "upContent" Decode.string
      downContent = get.Required.Field "downContent" Decode.string
      timestamp = get.Required.Field "timestamp" Decode.int64
    })

[<RequireQualifiedAccess;
  CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module MigrationRecord =

  let Encode: Encoder<MigrationRecord> =
    fun (record: MigrationRecord) ->
      Encode.object [
        "id", Encode.int64 record.id
        "name", Encode.string record.name
        "timestamp", Encode.int64 record.timestamp
      ]

  let Decode: Decoder<MigrationRecord> =
    Decode.object(fun get -> {
      id = get.Required.Field "id" Decode.int64
      name = get.Required.Field "name" Decode.string
      timestamp = get.Required.Field "timestamp" Decode.int64
    })

[<Interface>]
type SerializerEnv =

  abstract member ConfigurationSerializer: ConfigurationSerializer
  abstract member MigrationSerializer: MigrationSerializer
  abstract member MigrationRecordSerializer: MigrationRecordSerializer

module SerializerImpl =
  let configSerializer () =
    { new ConfigurationSerializer with
        member _.Decode
          (content: string)
          : Result<MigrondiConfig, SerializationError> =
          Decode.fromString MigrondiConfig.Decode content
          |> Result.mapError(fun error -> MalformedContent(content, error))

        member _.Encode(content: MigrondiConfig) : string =
          let content = MigrondiConfig.Encode content
          Encode.toString 2 content
    }

  let migrationRecordSerializer () =
    { new MigrationRecordSerializer with
        member _.Decode
          (content: string)
          : Result<MigrationRecord, SerializationError> =
          Decode.fromString MigrationRecord.Decode content
          |> Result.mapError(fun error -> MalformedContent(content, error))

        member _.Encode(content: MigrationRecord) : string =
          let content = MigrationRecord.Encode content
          Encode.toString 0 content
    }

  let migrationSerializer () =
    { new MigrationSerializer with
        member _.DecodeJson
          (content: string)
          : Result<Migration, SerializationError> =
          Decode.fromString Migration.DecodeJson content
          |> Result.mapError(fun error -> MalformedContent(content, error))

        member _.EncodeJson(content: Migration) : string =
          let content = Migration.EncodeJson content
          Encode.toString 0 content

        member _.EncodeText(content: Migration) : string =
          Migration.EncodeText content

        member _.DecodeText
          (content: string)
          : Result<Migration, SerializationError> =
          Migration.DecodeText content
          |> Result.mapError(fun error -> MalformedContent(content, error))


    }

type SerializerImpl =

  static member BuildEnv
    (
      ?configurationSerializer: ConfigurationSerializer,
      ?migrationRecordSerializer: MigrationRecordSerializer,
      ?migrationSerializer: MigrationSerializer
    ) =
    let configurationSerializer =
      configurationSerializer
      |> Option.defaultWith SerializerImpl.configSerializer

    let migrationRecordSerializer =
      migrationRecordSerializer
      |> Option.defaultWith SerializerImpl.migrationRecordSerializer

    let migrationSerializer =
      migrationSerializer
      |> Option.defaultWith SerializerImpl.migrationSerializer

    { new SerializerEnv with
        member _.ConfigurationSerializer: ConfigurationSerializer =
          configurationSerializer

        member _.MigrationRecordSerializer: MigrationRecordSerializer =
          migrationRecordSerializer

        member _.MigrationSerializer: MigrationSerializer = migrationSerializer
    }