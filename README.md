![.NET Core](https://github.com/AngelMunoz/Migrondi/workflows/.NET%20Core/badge.svg?branch=master)

# Migrondi
This is a pretty simple SQL Migrations tool that I've been live coding for the last week (Apr 6 - Apr 10)

This provides a way to execute SQL migrations against a database

## Usage
Grab the binary from the releases page or build from source

without any parameters it will give you the following screen

### Config File
to use this tool you need to supply a Json configuration file (the name must be `migrondi.json`)
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
    
    this is an absolute or relative path to where the migrations will be stored ***Note***: please include the trailing slash to prevent writing on the directory above of the one you pointed to.
- driver
    
    any of the following "mssql" "sqlite" "mysql" "postgres"
```
Migrondi 0.1.0
Copyright (C) 2020 Angel D. Munoz

ERROR(S):
  No verb selected.

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
-- ---------- SQLATOR:UP:1586550686936 --------------
-- Write your Up migrations here

-- ---------- SQLATOR:DOWN:1586550686936 --------------
-- Write how to revert the migration here
```
Please do not remove the `SQLATOR:UP:TIMESTAMP` and `SQLATOR:DOWN:TIMESTAMP` comments these are used to differentiate what to run when you run the `up` or `down` commands.

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
