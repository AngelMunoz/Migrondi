![.NET Core](https://github.com/AngelMunoz/Migrondi/workflows/.NET%20Core/badge.svg?branch=master)

# Migrondi

This is a pretty simple SQL Migrations tool that I've been live coding for the last week (Apr 6 - Apr 10) Migrondi provides a way to execute SQL migrations against a database

> You don't need a dotnet project/toolchain etc to use this, if you are using node/java/python/ruby that's completely fine this project works directly with your database and you shouldn't even need to have .net installed at all. If you want a brief tutorial/explanation you can check this post in [dev.to](https://dev.to/tunaxor/migrondi-simple-sql-migrations-tool-30lm)

## Usage

Grab the binary from the releases page or build from source and put it on your `PATH`, that way the command is available globally

## .net users

you can now install this as a global/local tool as well

```
dotnet tool install --global Migrondi --version 0.4.2
```

### Init

If you are starting from scratch you can run the init command to create the migrondi files and directories needed for the rest of the commands to work properly

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
Migrondi 0.4.0
Copyright (C) 2020 Angel D. Munoz

ERROR(S):
  No verb selected.

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

Use the Powershell build script or clone and run

```
dotnet publish -c Release -r <RID> --self-contained true -p:PublishSingleFile=true -o dist
```

replace RID and the angle brackets with any of the following

- [Windows RIDs](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#windows-rids)
- [Linux RIDs](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#linux-rids)
- [MacOS RIDs](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog#macos-rids)

this should give you a binary file in the `dist` directory, after that put it wherever you want and add it to your path and you can start using it
