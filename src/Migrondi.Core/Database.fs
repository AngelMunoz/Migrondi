namespace Migrondi.Core.Database

open Migrondi.Core
open System.Data

[<Interface>]
type DatabaseEnv =
  /// <summary>
  /// The driver that is used to interact with the database
  /// </summary>
  abstract member Driver: MigrondiDriver

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabase: unit -> Result<unit, string>

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigration: name: string -> MigrationRecord option

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastApplied: unit -> MigrationRecord option

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrations: unit -> MigrationRecord list

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  abstract member ApplyMigrations:
    migrations: Migration list -> Result<MigrationRecord list, string>

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  abstract member RollbackMigrations:
    migrations: Migration list -> Result<MigrationRecord list, string>

  /// <summary>
  /// Creates the required tables in the database.
  /// </summary>
  /// <returns>
  /// A result indicating whether the operation was successful or not
  /// </returns>
  abstract member SetupDatabaseAsync: unit -> Result<unit, string>

  ///<summary>
  /// Tries to find a migration by name in the migrations table
  /// </summary>
  /// <param name="name">The name of the migration to find</param>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindMigrationAsync:
    name: string -> Async<MigrationRecord option>

  /// <summary>
  /// Tries to find the last applied migration in the migrations table
  /// </summary>
  /// <returns>
  /// An optional migration record if the migration was found
  /// </returns>
  abstract member FindLastAppliedAsync: unit -> Async<MigrationRecord option>

  /// <summary>
  /// Lists the migrations that exist in the database
  /// </summary>
  /// <returns>
  /// A list of migration records that currently exist in the database
  /// </returns>
  abstract member ListMigrationsAsync: unit -> Async<MigrationRecord list>

  /// <summary>
  /// Applies the given migrations to the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were applied to the database
  /// </returns>
  abstract member ApplyMigrationsAsync:
    migrations: Migration list -> Async<Result<MigrationRecord list, string>>

  /// <summary>
  /// Rolls back the given migrations from the database
  /// </summary>
  /// <returns>
  /// A list of migration records that were rolled back from the database
  /// </returns>
  abstract member RollbackMigrationsAsync:
    migrations: Migration list -> Async<Result<MigrationRecord list, string>>


module MigrationsImpl =

  let setupDatabase
    (connection: IDbConnection)
    (driver: MigrondiDriver)
    (tableName: string)
    =
    failwith "Not Implemented"

  let findMigration (name: string) = failwith "Not Implemented"
  let FindLastApplied () = failwith "Not Implemented"
  let ListMigrations () = failwith "Not Implemented"
  let ApplyMigrations () = failwith "Not Implemented"

module MigrationsAsyncImpl =

  let setupDatabaseAsync () = failwith "Not Implemented"
  let FindMigrationAsync () = failwith "Not Implemented"
  let FindLastAppliedAsync () = failwith "Not Implemented"
  let ListMigrationsAsync () = failwith "Not Implemented"
  let ApplyMigrationsAsync () = failwith "Not Implemented"
  let RollbackMigrationsAsync () = failwith "Not Implemented"

[<Class>]
type DatabaseImpl =

  static member Build(connection: IDbConnection, config: MigrondiConfig) =
    { new DatabaseEnv with
        member this.Driver: MigrondiDriver = config.driver

        member this.SetupDatabase() : Result<unit, string> =
          MigrationsImpl.setupDatabase connection this.Driver config.tableName

        member this.FindLastApplied() : MigrationRecord option =
          failwith "Not Implemented"

        member this.ApplyMigrations
          (migrations: Migration list)
          : Result<MigrationRecord list, string> =
          failwith "Not Implemented"

        member this.FindMigration(name: string) : MigrationRecord option =
          failwith "Not Implemented"

        member this.ListMigrations() : MigrationRecord list =
          failwith "Not Implemented"

        member this.RollbackMigrations
          (migrations: Migration list)
          : Result<MigrationRecord list, string> =
          failwith "Not Implemented"

        member this.FindLastAppliedAsync() : Async<MigrationRecord option> =
          failwith "Not Implemented"

        member this.ApplyMigrationsAsync
          (migrations: Migration list)
          : Async<Result<MigrationRecord list, string>> =
          failwith "Not Implemented"

        member this.RollbackMigrationsAsync
          (migrations: Migration list)
          : Async<Result<MigrationRecord list, string>> =
          failwith "Not Implemented"

        member this.SetupDatabaseAsync() : Result<unit, string> =
          failwith "Not Implemented"

        member this.FindMigrationAsync
          (name: string)
          : Async<MigrationRecord option> =
          failwith "Not Implemented"

        member this.ListMigrationsAsync() : Async<MigrationRecord list> =
          failwith "Not Implemented"

    }