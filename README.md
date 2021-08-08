![.NET Core](https://github.com/AngelMunoz/Migrondi/workflows/.NET%20Core/badge.svg?branch=master)

> Currently Working on the next version to also add better support for the VSCode Extension!

# Migrondi

Migrondi is a SQL Migrations tool designed to be simple and execute simple migrations. Write SQL and execute SQL against your database.

> No need to install it, use it from [VSCode](https://marketplace.visualstudio.com/items?itemName=tunaxor-apps.migrondi-vscode)! https://github.com/AngelMunoz/migrondi-vscode

Migrondi Runs on `Linux-x64`, `Linux-arm64`, `Windows-x64`, and `MacOS-x64` (intel based)
## Install

### For Non .NET users

Grab the binary from the releases page or build from source and put it on your `PATH`, that way the command is available globally e.g.

```bash
# you can put this at the end of your ~/.bashrc
# $HOME/Apps/migrondi is a directory where you have downloaded your "Migrondi" binary
export MIGRONDI_HOME="$HOME/Apps/migrondi"
export PATH="$PATH:$MIGRONDI_HOME"
```

### For .NET users

you can now install this as a global/local tool as well

```
dotnet tool install --global Migrondi
```

## Usage

### Init

If you are starting from scratch you can run the init command to create the migrondi files and directories needed for the rest of the commands to work properly

```
~/Migrondi $ ./Migrondi init
Created /home/x/Migrondi/migrondi.json and /home/x/Migrondi/migrations
~/Migrondi $
```

```
PS C:\Users\x\Migrondi> ./Migrondi.exe init
Created C:\Users\x\Migrondi\migrondi.json and C:\Users\x\Migrondi\migrations\
PS C:\Users\x\Migrondi>
```

Then you can adapt the configuration as needed

### Config File

to use this tool you need to supply a JSON configuration file (the name must be `migrondi.json`)

```json
{
  "connection": "Data Source=Migrondi.db",
  "migrationsDir": "./migrations/",
  "driver": "sqlite"
}
```

- connection

  This is a IDbConnection compatible connection string (you can find examples in the follwing links)

  - [SqlServer](https://www.connectionstrings.com/sql-server/)
  - [SQLite](https://www.connectionstrings.com/sqlite/)
  - [MySQL](https://www.connectionstrings.com/mysql/)
  - [PostgreSQL](https://www.connectionstrings.com/postgresql/)

- migrationsDir

  this is an absolute or relative path to where the migrations will be stored **_Note_**: please include the trailing slash to prevent writing on the directory above of the one you pointed to. (if you use the init command, this is created for you)

- driver

  any of the following "mssql" "sqlite" "mysql" "postgres"

```
Migrondi 0.5.0
Copyright (C) 2020 Angel D. Munoz

  init       Creates basic files and directories to start using migrondi.

  new        Creates a new Migration file.

  up         Runs the migrations against the database.

  down       Rolls back migrations from the database.

  list       List the amount of migrations in the database.

  help       Display more information on a specific command.

  version    Display version information.
```

### New

To create a new migration file run `Migrondi.exe new -n CreateTodosTable` where `CreateTodosTable` is the name of your migration, you can replace that name with your migration name it will create a new file with a name like this:
`SampleMigration_1586550686936.sql` with the following contents

```sql
-- ---------- MIGRONDI:UP:1586550686936 --------------
-- Write your Up migrations here

-- ---------- MIGRONDI:DOWN:1586550686936 --------------
-- Write how to revert the migration here
```

Please do not remove the `MIGRONDI:UP:TIMESTAMP` and `MIGRONDI:DOWN:TIMESTAMP` comments these are used to differentiate what to run when you run the `up` or `down` commands.

### Up

To run your migrations against your database use the "up" command `Migrondi.exe up` you can use `-t <number>` to specify how many migrations you want to run

### Down

To rollback your migrations from your database use the "down" command `Migrondi.exe down` you can use `-t <number>` to specify how many migrations you want to roll back

### List

If you want to list migrations you can use the command "list" `Migrondi.exe list` with the following flag combinations

- `Migrondi.exe list --last true`
- `Migrondi.exe list --all true --missing true`
- `Migrondi.exe list --all true --missing false`

these will give you these outputs

- Last migration in the database
- All migrations that are missing
- All migrations present in the database

## Build

Use the build.fsx (`dotnet fsi build.fsx`) script or clone and run

```
dotnet publish -c Release -r <RID> --self-contained true -p:PublishSingleFile=true -o dist
```

replace RID and the angle brackets with any of the following

- [Windows RIDs](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#windows-rids)
- [Linux RIDs](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#linux-rids)
- [MacOS RIDs](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#macos-rids)

this should give you a binary file in the `dist` directory, after that put it wherever you want and add it to your path and you can start using it
