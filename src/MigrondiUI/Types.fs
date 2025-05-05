namespace MigrondiUI

open System
open Migrondi.Core

type LocalProject = {
  id: Guid
  name: string
  description: string option
  config: MigrondiConfig option
  migrondiConfigPath: string
}

type VirtualProject = {
  id: Guid
  name: string
  description: string option
  connection: string
  migrations: Guid
  tableName: string
  driver: MigrondiDriver
}

type Project =
  | Local of LocalProject
  | Virtual of VirtualProject


module Decoders =
  open JDeck

  let driverDecoder: Decoder<MigrondiDriver> =
    fun driver -> decode {
      let! driverStr = Required.string driver

      match driverStr with
      | "mysql" -> return MigrondiDriver.Mysql
      | "postgres" -> return MigrondiDriver.Postgresql
      | "mssql" -> return MigrondiDriver.Mssql
      | "sqlite" -> return MigrondiDriver.Sqlite
      | d ->
        return! DecodeError.ofError(driver, $"Invalid driver %s{d}") |> Error
    }

  let migrondiConfigDecoder: Decoder<MigrondiConfig> =
    fun config -> decode {
      let! connection =
        config |> Required.Property.get("connection", Required.string)

      and! migrations =
        config |> Required.Property.get("migrations", Required.string)

      and! tableName =
        config |> Required.Property.get("tableName", Required.string)

      and! driver = config |> Required.Property.get("driver", driverDecoder)

      return {
        connection = connection
        migrations = migrations
        tableName = tableName
        driver = driver
      }
    }

  let driverEncoder: Encoder<MigrondiDriver> =
    fun driver ->
      match driver with
      | MigrondiDriver.Mysql -> Encode.string "mysql"
      | MigrondiDriver.Postgresql -> Encode.string "postgres"
      | MigrondiDriver.Mssql -> Encode.string "mssql"
      | MigrondiDriver.Sqlite -> Encode.string "sqlite"

  let migrondiConfigEncoder: Encoder<MigrondiConfig> =
    fun config ->
      Json.object [
        "connection", Encode.string config.connection
        "migrations", Encode.string config.migrations
        "tableName", Encode.string config.tableName
        "driver", driverEncoder config.driver
      ]

[<AutoOpen>]
module ProjectExtensions =
  open JDeck

  type Project with
    member this.Id =
      match this with
      | Local p -> p.id
      | Virtual p -> p.id

    member this.Name =
      match this with
      | Local p -> p.name
      | Virtual p -> p.name

    member this.Description =
      match this with
      | Local p -> p.description
      | Virtual p -> p.description


  type MigrondiConfig with

    member this.FromString config =
      Decoding.fromString(config, Decoders.migrondiConfigDecoder)
      |> Result.toOption


// TODO: Add this.Migrations
// TODO: Add this.Config
// These have to be added later down where we are able to resolve the migrations whether they are local or virtual
