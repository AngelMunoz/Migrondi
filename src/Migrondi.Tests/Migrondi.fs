namespace Migrondi.Tests.Migrondi

open System

open Microsoft.VisualStudio.TestTools.UnitTesting

open RepoDb

open FsToolkit.ErrorHandling

open Migrondi.Core
open Migrondi.Core.Serialization
open Migrondi.Core.FileSystem
open Migrondi.Core.Database
open Migrondi.Core.Migrondi
open System.IO

module DependenciesHelper =
  open System.Threading.Tasks

  let getCustomDb (amount: int) =
    let records =
      [
        for i in 1..amount do
          let tableName = $"table_{i}"

          let timestamp =
            DateTimeOffset(DateTime.Today.AddMinutes(i))
              .ToUnixTimeMilliseconds()

          let recordId = i - 1

          {
            id = recordId
            name = tableName
            timestamp = timestamp
          }
      ]
      |> List.sortByDescending(fun record -> record.timestamp)

    { new DatabaseEnv with
        member _.SetupDatabase() : unit = ()

        member _.SetupDatabaseAsync
          (cancellationToken: Threading.CancellationToken option)
          : Task =
          Task.FromResult(())

        member _.FindLastApplied() : MigrationRecord option =
          records |> List.tryLast

        member _.FindLastAppliedAsync
          (cancellationToken: Threading.CancellationToken option)
          : Task<MigrationRecord option> =
          records |> List.tryLast |> Task.FromResult

        member _.FindMigration(name: string) : MigrationRecord option =
          records |> List.tryFind(fun record -> record.name = name)

        member _.FindMigrationAsync
          (
            name: string,
            cancellationToken: Threading.CancellationToken option
          ) : Task<MigrationRecord option> =
          records
          |> List.tryFind(fun record -> record.name = name)
          |> Task.FromResult

        member _.ListMigrations
          ()
          : Collections.Generic.IReadOnlyList<MigrationRecord> =
          records

        member _.ListMigrationsAsync
          (cancellationToken: Threading.CancellationToken option)
          : Task<Collections.Generic.IReadOnlyList<MigrationRecord>> =
          records :> Collections.Generic.IReadOnlyList<MigrationRecord>
          |> Task.FromResult

        member _.ApplyMigrations
          (migrations: seq<Migration>)
          : Collections.Generic.IReadOnlyList<MigrationRecord> =
          match
            migrations
            |> Seq.tryFind(fun m ->
              m.upContent = "this should fail"
              || m.downContent = "this should fail"
            )
          with
          | None -> records
          | Some m -> raise(MigrationApplicationFailed m)

        member _.ApplyMigrationsAsync
          (
            migrations: seq<Migration>,
            cancellationToken: Threading.CancellationToken option
          ) : Task<Collections.Generic.IReadOnlyList<MigrationRecord>> =
          task {
            return
              match
                migrations
                |> Seq.tryFind(fun m ->
                  m.upContent = "this should fail"
                  || m.downContent = "this should fail"
                )
              with
              | None -> records
              | Some m -> raise(MigrationApplicationFailed m)
          }

        member _.RollbackMigrations
          (migrations: seq<Migration>)
          : Collections.Generic.IReadOnlyList<MigrationRecord> =
          match
            migrations
            |> Seq.tryFind(fun m ->
              m.upContent = "this should fail"
              || m.downContent = "this should fail"
            )
          with
          | None ->
            migrations
            |> Seq.mapi(fun i m -> {
              id = int64(i + 1)
              name = m.name
              timestamp = m.timestamp
            })
            |> List.ofSeq
            :> Collections.Generic.IReadOnlyList<MigrationRecord>
          | Some m -> raise(MigrationApplicationFailed m)

        member _.RollbackMigrationsAsync
          (
            migrations: seq<Migration>,
            cancellationToken: Threading.CancellationToken option
          ) : Task<Collections.Generic.IReadOnlyList<MigrationRecord>> =
          task {
            return
              match
                migrations
                |> Seq.tryFind(fun m ->
                  m.upContent = "this should fail"
                  || m.downContent = "this should fail"
                )
              with
              | None ->
                migrations
                |> Seq.mapi(fun i m -> {
                  id = int64(i + 1)
                  name = m.name
                  timestamp = m.timestamp
                })
                |> List.ofSeq
                :> Collections.Generic.IReadOnlyList<MigrationRecord>
              | Some m -> raise(MigrationApplicationFailed m)
          }
    }

  let getCustomFs (amount: int) (shouldFail: bool) =

    let migrations =
      [
        for i in 1..amount do
          let tableName = $"table_{i}"

          let timestamp =
            DateTimeOffset(DateTime.Today.AddMinutes(i))
              .ToUnixTimeMilliseconds()

          let upContent =
            $"create table {tableName} (id int not null primary key);"

          let downContent = $"drop table {tableName};"

          if (shouldFail && (amount / 2) = i) then
            {
              name = tableName
              timestamp = timestamp
              upContent = "this should fail"
              downContent = "this should fail"
            }
          else
            {
              name = tableName
              timestamp = timestamp
              upContent = upContent
              downContent = downContent
            }
      ]
      |> List.sortByDescending(fun migration -> migration.timestamp)

    { new FileSystemEnv with
        member _.ListMigrations
          (readFrom: string)
          : Collections.Generic.IReadOnlyList<Migration> =
          migrations

        member _.ListMigrationsAsync
          (
            readFrom: string,
            cancellationToken: Threading.CancellationToken option
          ) : Threading.Tasks.Task<Collections.Generic.IReadOnlyList<Migration>> =
          Task.FromResult(migrations)

        member _.ReadMigration(readFrom: string) : Migration =
          match
            migrations
            |> List.tryFind(fun migration -> migration.name = readFrom)
          with
          | Some migration -> migration
          | None -> raise(SourceNotFound(readFrom, readFrom))

        member _.ReadMigrationAsync
          (
            readFrom: string,
            cancellationToken: Threading.CancellationToken option
          ) : Threading.Tasks.Task<Migration> =
          match
            migrations
            |> List.tryFind(fun migration -> migration.name = readFrom)
          with
          | Some migration -> Task.FromResult(migration)
          | None -> raise(SourceNotFound(readFrom, readFrom))

        member _.ReadConfiguration(readFrom: string) : MigrondiConfig =
          failwith "Not Implemented"

        member _.ReadConfigurationAsync
          (
            readFrom: string,
            cancellationToken: Threading.CancellationToken option
          ) : Threading.Tasks.Task<MigrondiConfig> =
          failwith "Not Implemented"

        member _.WriteConfiguration
          (
            config: MigrondiConfig,
            writeTo: string
          ) : unit =
          failwith "Not Implemented"

        member _.WriteConfigurationAsync
          (
            config: MigrondiConfig,
            writeTo: string,
            cancellationToken: Threading.CancellationToken option
          ) : Threading.Tasks.Task =
          failwith "Not Implemented"

        member _.WriteMigration(migration: Migration, writeTo: string) : unit =
          failwith "Not Implemented"

        member _.WriteMigrationAsync
          (
            migration: Migration,
            writeTo: string,
            cancellationToken: Threading.CancellationToken option
          ) : Threading.Tasks.Task =
          failwith "Not Implemented"
    }

[<TestClass>]
type MigrondiServiceTests() =


  [<TestMethod>]
  member _.``RunUp should apply all pending migrations``() =
    let fs = DependenciesHelper.getCustomFs 10 false
    let db = DependenciesHelper.getCustomDb 10

    let migrondi: MigrondiService =
      Migrondi(Directory.GetCurrentDirectory(), fs = fs, db = db)

    migrondi.RunUp