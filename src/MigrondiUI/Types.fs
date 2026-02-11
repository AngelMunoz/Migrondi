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
  tableName: string
  driver: MigrondiDriver
  projectId: Guid
}

type VirtualMigration = {
  id: Guid
  name: string
  timestamp: int64
  upContent: string
  downContent: string
  projectId: Guid
  manualTransaction: bool
}

type Project =
  | Local of LocalProject
  | Virtual of VirtualProject

[<AutoOpen>]
module ProjectExtensions =

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


  type VirtualProject with

    member this.ToMigrondiConfig() = {
      connection = this.connection
      migrations = this.id.ToString()
      tableName = this.tableName
      driver = this.driver
    }

  type VirtualMigration with
    member this.ToMigration() : Migration = {
      name = this.name
      timestamp = this.timestamp
      upContent = this.upContent
      downContent = this.downContent
      manualTransaction = this.manualTransaction
    }
