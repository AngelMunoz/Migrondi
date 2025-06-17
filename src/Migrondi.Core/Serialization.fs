namespace Migrondi.Core.Serialization


open System.Text
open System.Text.RegularExpressions
open System.Runtime.InteropServices

open Thoth.Json.Net

open FsToolkit.ErrorHandling

open Migrondi.Core


[<RequireQualifiedAccess;
  CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module private MigrondiDriver =
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
module private MigrondiConfig =

  let Encode: Encoder<MigrondiConfig> =
    fun (config: MigrondiConfig) ->
      Encode.object [
        "connection", Encode.string config.connection
        "migrations", Encode.string config.migrations
        "tableName", Encode.string config.tableName
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

module private Migration =
  let migrationDelimiter (key: string, value: string option) =
    let start =
      match value with
      | Some _ -> "-- "
      | None -> "-- ---------- "

    let value =
      match value with
      | Some value -> $"={value}"
      | None -> " ----------"


    $"{start}MIGRONDI:%s{key}{value}"

  let EncodeJson: Encoder<Migration> =
    fun (migration: Migration) ->
      Encode.object [
        "name", Encode.string migration.name
        "timestamp", Encode.int64 migration.timestamp
        "upContent", Encode.string migration.upContent
        "downContent", Encode.string migration.downContent
        "manualTransaction", Encode.bool migration.manualTransaction
      ]

  let EncodeText (migration: Migration) : string =
    // GOAL: Migrations Format V1 have to be encoded like this:
    // -- Do not remove MIGRONDI comments.
    // -- MIGRONDI:NAME=AddUsersTable
    // -- MIGRONDI:TIMESTAMP=1586550686936
    // -- ---------- MIGRONDI:UP ----------
    // -- Write your Up migrations here
    //
    // -- ---------- MIGRONDI:UP ----------
    // -- Write how to revert the migration here

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
        // manually set the line endings to \n
        .Append(name)
        .Append('\n')
        .Append(timestamp)
        .Append('\n')
        .Append(up)
        .Append('\n')
        .Append(migration.upContent)
        .Append('\n')
        .Append(down)
        .Append('\n')
        .Append(migration.downContent)
        .Append('\n')
        .ToString()

    content

  let DecodeTextV0
    (content: string)
    (name: string option)
    : Result<Migration, string> =
    result {
      // Migrations Format V0 are already encoded like this:
      //-- ---------- MIGRONDI:UP:1586550686936 --------------
      //-- Write your Up migrations here
      //
      //-- ---------- MIGRONDI:DOWN:1586550686936 --------------
      //-- Write how to revert the migration here
      let! name =
        name |> Result.requireSome "Migration name is required for format v0"

      let matcher =
        Regex(
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
        |> _.Index

      let downIndex =
        collection
        |> Seq.find(fun value -> value.Groups["Identifier"].Value = "DOWN")
        |> _.Index

      // we've found the up delimiter, skip it and go straight for the content
      let slicedUp = content[upIndex .. downIndex - 1]
      let fromUp = slicedUp.IndexOf('\n') + 1
      // we've found the down delimiter, skip it and go straight for the content
      let slicedDown = content[downIndex..]
      let fromDown = content.Substring(downIndex).IndexOf('\n') + 1

      let! timestamp =
        try
          collection[0].Groups["Timestamp"].Value |> int64 |> Ok
        with ex ->
          Error $"Invalid timestamp: {ex.Message}"

      let! upContent =
        try
          slicedUp[fromUp..] |> Ok
        with ex ->
          Error $"Invalid up content: {ex.Message}"

      let! downContent =
        try
          slicedDown[fromDown..] |> Ok
        with ex ->
          Error $"Invalid down content: {ex.Message}"

      return {
        name = name
        upContent = upContent.Trim()
        downContent = downContent.Trim()
        timestamp = timestamp
        manualTransaction = false
      }
    }

  let DecodeTextV1 (content: string) : Result<Migration, string> = result {
    // GOAL: Migrations Format V1 are encoded like this:
    // -- Do not remove MIGRONDI comments.
    // -- MIGRONDI:Name=AddUsersTable
    // -- MIGRONDI:TIMESTAMP=1586550686936
    // -- MIGRONDI:ManualTransaction=true
    // -- ---------- MIGRONDI:UP ----------
    // -- Write your Up migrations here
    //
    // -- ---------- MIGRONDI:UP ----------
    // -- Write how to revert the migration here
    let upDownMatcher =
      Regex(
        "-- ---------- MIGRONDI:(?<Identifier>UP|DOWN) ----------",
        RegexOptions.Multiline
      )

    let metadataMatcher =
      Regex(
        "-- MIGRONDI:(?<Key>[a-zA-Z0-9_-]+)=(?<Value>[a-zA-Z0-9_-]+)",
        RegexOptions.Multiline
      )

    let upDownCollection = upDownMatcher.Matches(content)
    let metadataCollection = metadataMatcher.Matches(content)

    let! name =
      metadataCollection
      |> Seq.find(fun value -> value.Groups["Key"].Value = "NAME")
      |> fun value -> value.Groups["Value"].Value |> Option.ofObj
      |> Result.requireSome "Missing Migration Name In metadata"

    let! timestamp =
      metadataCollection
      |> Seq.find(fun value -> value.Groups["Key"].Value = "TIMESTAMP")
      |> fun value -> value.Groups["Value"].Value |> Option.ofObj
      |> Result.requireSome "Missing Migration Timestamp In metadata"
      |> Result.bind(fun value ->
        try
          int64 value |> Ok
        with ex ->
          Error $"Invalid timestamp: {ex.Message}"
      )

    let manualTransaction =
      let parseTransaction (value: string) =
        let value = if value = null then "" else value

        match value.ToLowerInvariant() with
        | "true" -> true
        | "false" -> false
        | _ -> false

      metadataCollection
      |> Seq.tryFind(fun value ->
        value.Groups["Key"].Value = "ManualTransaction"
      )
      |> fun value ->
          match value with
          | Some value -> value.Groups["Value"].Value |> parseTransaction
          | None -> false

    do!
      upDownCollection.Count = 2
      |> Result.requireTrue "Invalid Migrations Format"
      |> Result.ignore

    let upIndex =
      upDownCollection
      |> Seq.find(fun value -> value.Groups["Identifier"].Value = "UP")
      |> _.Index

    let downIndex =
      upDownCollection
      |> Seq.find(fun value -> value.Groups["Identifier"].Value = "DOWN")
      |> _.Index

    // we've found the up delimiter, skip it and go straight for the content
    let slicedUp = content[upIndex .. downIndex - 1]
    let fromUp = slicedUp.IndexOf('\n') + 1
    // we've found the down delimiter, skip it and go straight for the content
    let slicedDown = content[downIndex..]
    let fromDown = content.Substring(downIndex).IndexOf('\n') + 1

    let! upContent =
      try
        slicedUp[fromUp..] |> Ok
      with ex ->
        Error $"Invalid up content: {ex.Message}"

    let! downContent =
      try
        slicedDown[fromDown..] |> Ok
      with ex ->
        Error $"Invalid down content: {ex.Message}"

    return {
      name = name
      upContent = upContent.Trim()
      downContent = downContent.Trim()
      timestamp = timestamp
      manualTransaction = manualTransaction
    }
  }

  let DecodeText
    (content: string, name: string option)
    : Result<Migration, string> =
    let matcher =
      Regex(
        "-- ---------- MIGRONDI:(?<Identifier>UP|DOWN):(?<Timestamp>[0-9]+) ----------",
        RegexOptions.Multiline
      )

    // If it has at least one match it's a v0 migration
    if matcher.Matches(content) |> Seq.length > 0 then
      DecodeTextV0 content name
    else
      DecodeTextV1 content



  let DecodeJson: Decoder<Migration> =
    Decode.object(fun get -> {
      name = get.Required.Field "name" Decode.string
      upContent = get.Required.Field "upContent" Decode.string
      downContent = get.Required.Field "downContent" Decode.string
      timestamp = get.Required.Field "timestamp" Decode.int64
      manualTransaction = get.Required.Field "manualTransaction" Decode.bool
    })

[<RequireQualifiedAccess;
  CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module private MigrationRecord =

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
type IMiMigrationSerializer =

  abstract member EncodeJson: content: Migration -> string
  abstract member EncodeText: content: Migration -> string
  abstract member DecodeJson: content: string -> Migration

  abstract member DecodeText:
    content: string * [<Optional>] ?migrationName: string -> Migration

  abstract member EncodeMigrationRecord: content: MigrationRecord -> string
  abstract member DecodeMigrationRecord: content: string -> MigrationRecord

[<Interface>]
type IMiConfigurationSerializer =

  abstract member Encode: content: MigrondiConfig -> string
  abstract member Decode: content: string -> MigrondiConfig

[<Class>]
type MigrondiSerializer() =

  interface IMiMigrationSerializer with
    member _.DecodeJson(content: string) : Migration =
      Decode.fromString Migration.DecodeJson content
      |> function
        | Ok value -> value
        | Error err -> DeserializationFailed(content, err) |> raise

    member _.EncodeJson(content: Migration) : string =
      let content = Migration.EncodeJson content
      Encode.toString 0 content

    member _.EncodeText(content: Migration) : string =
      Migration.EncodeText content

    member _.DecodeText
      (content: string, migrationName: string option)
      : Migration =
      Migration.DecodeText(content, migrationName)
      |> function
        | Ok value -> value
        | Error err -> DeserializationFailed(content, err) |> raise

    member _.DecodeMigrationRecord(content: string) : MigrationRecord =
      Decode.fromString MigrationRecord.Decode content
      |> function
        | Ok value -> value
        | Error err -> DeserializationFailed(content, err) |> raise

    member _.EncodeMigrationRecord(content: MigrationRecord) : string =
      let content = MigrationRecord.Encode content
      Encode.toString 0 content

  interface IMiConfigurationSerializer with
    member _.Decode(content: string) : MigrondiConfig =
      Decode.fromString MigrondiConfig.Decode content
      |> function
        | Ok value -> value
        | Error err -> DeserializationFailed(content, err) |> raise

    member _.Encode(content: MigrondiConfig) : string =
      let content = MigrondiConfig.Encode content
      Encode.toString 2 content