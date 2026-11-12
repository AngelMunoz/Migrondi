namespace MigrondiUI.Logic

module MigrationRunner =

  [<Struct>]
  type RunMigrationKind =
    | Up
    | Down
    | DryUp
    | DryDown

  type CurrentShow =
    | Migrations
    | DryRun of RunMigrationKind
    | ExceptionThrown of exn
