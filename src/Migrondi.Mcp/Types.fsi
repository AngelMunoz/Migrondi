namespace MigrondiUI

type LocalProject = {
  id: System.Guid
  name: string
  description: string option
  config: Migrondi.Core.MigrondiConfig option
  migrondiConfigPath: string
}

type VirtualProject = {
  id: System.Guid
  name: string
  description: string option
  connection: string
  tableName: string
  driver: Migrondi.Core.MigrondiDriver
  projectId: System.Guid
}

type VirtualMigration = {
  id: System.Guid
  name: string
  timestamp: int64
  upContent: string
  downContent: string
  projectId: System.Guid
  manualTransaction: bool
}

type Project =
  | Local of LocalProject
  | Virtual of VirtualProject

[<AutoOpen>]
module ProjectExtensions =
  type Project with

    member Id: System.Guid

  type Project with

    member Name: string

  type Project with

    member Description: string option

  type VirtualProject with

    member ToMigrondiConfig: unit -> Migrondi.Core.MigrondiConfig

  type VirtualMigration with

    member ToMigration: unit -> Migrondi.Core.Migration
