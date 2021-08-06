namespace Migrondi

module Options =
    open CommandLine

    [<CLIMutable; Verb("init", HelpText = "Creates basic files and directories to start using migrondi.")>]
    type InitOptions =
        { [<Option('p', "path", Required = false, HelpText = "Where should the migrondi.json should be created")>]
          path: string
          [<Option("no-color",
                   Required = false,
                   HelpText = "Write to the console without coloring enabled",
                   Default = false)>]
          noColor: bool
          [<Option('j', "json", Required = false, HelpText = "Output to the console with a json format", Default = false)>]
          json: bool }

    [<CLIMutable; Verb("new", HelpText = "Creates a new Migration file.")>]
    type NewOptions =
        { [<Option('n', "name", Required = true, HelpText = "Friendly Name of the Migration you want to create.")>]
          name: string
          [<Option("no-color",
                   Required = false,
                   HelpText = "Write to the console without coloring enabled",
                   Default = false)>]
          noColor: bool
          [<Option('j', "json", Required = false, HelpText = "Output to the console with a json format", Default = false)>]
          json: bool }

    [<CLIMutable; Verb("up", HelpText = "Runs the migrations against the database.")>]
    type UpOptions =
        { [<Option('t', "total", Required = false, HelpText = "Amount of migrations to run up.", Default = 1)>]
          total: int
          [<Option('d',
                   "dry-run",
                   Required = false,
                   HelpText = "Prints to the console what is going to be run against the database",
                   Default = true)>]
          dryRun: bool
          [<Option("no-color",
                   Required = false,
                   HelpText = "Write to the console without coloring enabled",
                   Default = false)>]
          noColor: bool
          [<Option('j', "json", Required = false, HelpText = "Output to the console with a json format", Default = false)>]
          json: bool }

    [<CLIMutable; Verb("down", HelpText = "Rolls back migrations from the database.")>]
    type DownOptions =
        { [<Option('t', "total", Required = false, HelpText = "Amount of migrations to run down.", Default = 1)>]
          total: int
          [<Option('d',
                   "dry-run",
                   Required = false,
                   HelpText = "Prints to the console what is going to be run against the database",
                   Default = true)>]
          dryRun: bool
          [<Option("no-color",
                   Required = false,
                   HelpText = "Write to the console without coloring enabled",
                   Default = false)>]
          noColor: bool
          [<Option('j', "json", Required = false, HelpText = "Output to the console with a json format", Default = false)>]
          json: bool }

    [<CLIMutable; Verb("list", HelpText = "List the amount of migrations in the database.")>]
    type ListOptions =
        { [<Option('a',
                   "all",
                   Required = false,
                   HelpText = "Shows every migration ran against the database.",
                   Default = false)>]
          all: bool
          [<Option('m',
                   "missing",
                   Required = false,
                   HelpText = "Shows the migrations that are pending to run.",
                   Default = false)>]
          missing: bool
          [<Option('l',
                   "last",
                   Required = false,
                   HelpText = "Shows the last migration run agains the database.",
                   Default = true)>]
          last: bool
          [<Option("no-color",
                   Required = false,
                   HelpText = "Write to the console without coloring enabled",
                   Default = false)>]
          noColor: bool
          [<Option('j', "json", Required = false, HelpText = "Output to the console with a json format", Default = false)>]
          json: bool }
