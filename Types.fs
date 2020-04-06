namespace Sqlator

module Types =

    [<CLIMutable>]
    type Migration =
        { Id: int64
          Name: string
          Date: int64 }

    type MigrationFile =
        { name: string
          version: int
          content: string }
