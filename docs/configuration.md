---
title: Configuration
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

  any of the following "mssql" "sqlite" "mysql" "postgres"
