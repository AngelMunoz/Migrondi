namespace Sqlator

module Types =

    [<CLIMutable>]
    type SqlatorConfig =
        { connection: string
          migrationsDir: string }

    [<CLIMutable>]
    type Migration =
        { Id: int64
          Name: string
          Date: int64 }

    type MigrationFile =
        { name: string
          timestamp: int64
          upContent: string
          downContent: string }

    [<RequireQualifiedAccess>]
    type MigrationType =
        | Up
        | Down
