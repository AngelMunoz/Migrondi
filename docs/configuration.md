---
title: Configuration
category: Overview
categoryindex: 2
---

### Migrondi.json

To be able to use this tool you need to supply a JSON configuration file named `migrondi.json` this file can be on the root of your project or in a dedicated directory such as `migrations`.

```json
{
  "connection": "Data Source=./migrondi.db",
  "migrations": "./migrations",
  "tableName": "__migrondi_migrations",
  "driver": "sqlite"
}
```

- connection

  This is a IDbConnection compatible connection string (you can find examples in the follwing links)

  - [SqlServer](https://www.connectionstrings.com/sql-server/)
  - [SQLite](https://www.connectionstrings.com/sqlite/)
  - [MySQL](https://www.connectionstrings.com/mysql/)
  - [PostgreSQL](https://www.connectionstrings.com/postgresql/)

- migrations

  this is an absolute or relative path to where the migrations will be stored **_Note_**: please include the trailing slash to prevent writing on the directory above of the one you pointed to. (if you use the init command, this is created for you)

- tableName

  this is the name of the table that will be used to store the migrations, this table will be created for you if it does not exist.

- driver

  any of the following "mssql" "sqlite" "mysql" "postgresql"

### SQLite Path Resolution

When using SQLite with a relative connection string (e.g., `"Data Source=./migrondi.db"`), Migrondi automatically resolves the database path relative to the root directory:

```json
{
  "connection": "Data Source=./migrondi.db",
  "migrations": "./migrations",
  "driver": "sqlite"
}
```

If the root directory is `/my/app`, the database path will be resolved to `/my/app/migrondi.db`. This allows your project to be portable across different machines.

Absolute paths are left unchanged.

## Environment Variables and CLI options

The following environment variables can be used to configure migrondi:

- `MIGRONDI_CONNECTION_STRING`: The connection string to the database
- `MIGRONDI_MIGRATIONS`: The directory where the migration files are stored
- `MIGRONDI_TABLE_NAME`: The name of the table that will store the migrations
- `MIGRONDI_DRIVER`: The driver to use for the database connection

You can pass the options via the CLI:

- `--connection`: The connection string to the database
- `--migrations`: The directory where the migration files are stored
- `--table-name`: The name of the table that will store the migrations
- `--driver`: The driver to use for the database connection

For example:

```
migrondi --driver sqlite --connection "Data Source=./migrondi.db" up --dry
```

> **_NOTE_**: The configuration flags **MUST** be passed before the command, otherwise they will be interpreted as arguments for the command and will fail.

The priority of the configuration is as follows (last one wins):

migrondi.json < Environment variables < CLI options
