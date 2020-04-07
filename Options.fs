namespace Sqlator


module Options =
    open CommandLine

    [<Verb("new", HelpText = "Creates a new Migration file.")>]
    type NewOptions =
        { [<Option('n', "name", Required = true, HelpText = "Friendly Name of the Migration you want to create.")>]
          name: string }

    [<Verb("up", HelpText = "Runs the migrations against the database.")>]
    type UpOptions =
        { [<Option('t', "total", Required = false, HelpText = "Amount of migrations to run up.")>]
          total: int option }

    [<Verb("down", HelpText = "Rolls back migrations from the database.")>]
    type DownOptions =
        { [<Option('t', "total", Required = false, HelpText = "Amount of migrations to run down.")>]
          total: int option }

    [<Verb("list", HelpText = "List the amount of migrations in the database.")>]
    type ListOptions =
        { [<Option('t', "total", Required = false, HelpText = "Shows every migration ran against the database.")>]
          total: bool option
          [<Option('m', "missing", Required = false, HelpText = "Shows the migrations that are pending to run.")>]
          missing: bool option
          [<Option('l', "last", Required = false, HelpText = "Shows the last migration run agains the database.")>]
          last: int option }
