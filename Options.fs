namespace Sqlator



module Options =
    open System
    open CommandLine

    [<Verb("new", HelpText = "Creates a new Migration file.")>]
    type NewOptions =
        { [<Option('n', "name", Required = true, HelpText = "Friendly Name of the Migration you want to create.")>]
          name: string }

    [<Verb("up", HelpText = "Runs the migrations against the database.")>]
    type UpOptions =
        { [<Option('t', "total", Required = false, HelpText = "Amount of migrations to run up.")>]
          total: Nullable<int> }

    [<Verb("down", HelpText = "Rolls back migrations from the database.")>]
    type DownOptions =
        { [<Option('t', "total", Required = false, HelpText = "Amount of migrations to run down.")>]
          total: Nullable<int> }

    [<Verb("list", HelpText = "List the amount of migrations in the database.")>]
    type ListOptions =
        { [<Option('a', "all", Required = false, HelpText = "Shows every migration ran against the database.")>]
          all: Nullable<bool>
          [<Option('m', "missing", Required = false, HelpText = "Shows the migrations that are pending to run.")>]
          missing: Nullable<bool>
          [<Option('l', "last", Required = false, HelpText = "Shows the last migration run agains the database.")>]
          last: Nullable<bool> }
