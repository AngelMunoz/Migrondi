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
          version: int
          content: string }
