namespace MigrondiUI

open System
open Migrondi.Core

type LocalProject = {
  id: Guid
  name: string
  description: string option
  config: MigrondiConfig option
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


// TODO: Add this.Migrations
// TODO: Add this.Config
// These have to be added later down where we are able to resolve the migrations whether they are local or virtual
