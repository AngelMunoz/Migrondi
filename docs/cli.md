---
title: Command Line Interface
category: Overview
categoryindex: 2
---

Migrondi takes the directory where it was invoked as the root of the project, it will also try to read a full `migrondi.json` file right there.
If the file is not found or it is not valid it will default to the following configuration:

```json
{
  "connection": "Data Source=./migrondi.db",
  "migrations": "./migrations",
  "tableName": "__migrondi_migrations",
  "driver": "sqlite"
}
```

> **_NOTE_**: Partial configuration is not supported yet, you either provide a full configuration file or use the environment variables to override the default configuration.

## Getting started

migrondi offers the `init` command

```
$ migrondi init
[21:59:24 INF] Initializing a new migrondi project at: C:\Users\scyth\migrondi-sample.
[21:59:24 INF] migrondi.json and migrations directory created successfully.
```

This will create the `migrondi.json` file and the `migrations` directory for you.

The next logical step would be to create a migration file, you can do that with the `new` command

```
$ migrondi new create-users-table
[22:00:58 INF] Creating a new migration with name: create-users-table.
[22:00:58 INF] Migration create-users-table_1695873658869.sql created successfully.
```

All of the migrondi migrations have the following naming convention:

- `{timestamp}_{name}.sql`

The name is the one you specify as the argument for the `new` command and should not be changed once it has been applied to the database.

The timestamp is a unix miliseconds timestamp that is used to order the migrations.

The latest date is always at the top while the oldest is at the bottom.

### Migration Format Versions

Migrondi supports two migration file formats:

**V1 Format (current)**:
- Filename: `{timestamp}_{name}.sql`
- Required headers: `-- MIGRONDI:NAME={name}` and `-- MIGRONDI:TIMESTAMP={timestamp}`
- Optional header: `-- MIGRONDI:ManualTransaction=true`

**V0 Format (deprecated)**:
- Filename: `{name}_{timestamp}.sql`
- Content marker: `-- ---------- MIGRONDI:UP:{timestamp} ----------`

Migrondi automatically detects and handles both formats. You don't need to manually migrate your V0 files - they will continue to work. However, new migrations created with the `new` command will use the V1 format.

This should have created a new file in the `migrations` directory with the following content:

```sql
-- MIGRONDI:NAME=create-users-table_1695873658869.sql
-- MIGRONDI:TIMESTAMP=1695873658869
-- ---------- MIGRONDI:UP ----------
-- Add your SQL migration code below. You can delete this line but do not delete the comments above.


-- ---------- MIGRONDI:DOWN ----------
-- Add your SQL rollback code below. You can delete this line but do not delete the comment above.

```

The first lines are used by migrondi to keep track of the migration file, please do not delete them.
The markers `MIGRONDI:UP` and `MIGRONDI:DOWN` are used to separate the migration code from the rollback code, and you should not delete those as well.

### Manual Transactions

By default, Migrondi wraps each migration in a transaction. If you need to manage your own transactions (e.g., for `CREATE INDEX CONCURRENTLY` or multi-step processes), you can add the following header:

`-- MIGRONDI:ManualTransaction=true`

With those elements present in the migration file in that order you can start writing some SQL code to create your table.

Example:

```sql
-- MIGRONDI:NAME=create-users-table_1695873658869.sql
-- MIGRONDI:TIMESTAMP=1695873658869
-- ---------- MIGRONDI:UP ----------
create table users (
    id integer primary key,
    name text not null,
    email text not null,
    password text not null,
    created_at datetime not null,
    updated_at datetime not null
);

-- ---------- MIGRONDI:DOWN ----------
drop table users;

```

Before we apply this to our database let's check what's the status of our current migrations

```text
$ migrondi list
                                 All Migrations
┌─────────┬──────────────────────────────────┬──────────────────────────────────┐
│ Status  │ Name                             │ Date Created                     │
├─────────┼──────────────────────────────────┼──────────────────────────────────┤
│ Pending │ create-users-table_1695873658869 │ 27/09/2023 10:00:58 p. m. -06:00 │
└─────────┴──────────────────────────────────┴──────────────────────────────────┘
```

> Note that this should render fine in your console

Great! now that we have a migration file we can apply it to the database with the `up` command

```
$ migrondi up
[22:10:01 INF] Running '1' migrations.
[22:10:01 INF] Applied migration 'create-users-table_1695873658869' successfully.
```

This will run all the migrations that have not been applied to the database yet.

```text
$ migrondi list
                                 All Migrations
┌─────────┬──────────────────────────────────┬──────────────────────────────────┐
│ Status  │ Name                             │ Date Created                     │
├─────────┼──────────────────────────────────┼──────────────────────────────────┤
│ Applied │ create-users-table_1695873658869 │ 27/09/2023 10:00:58 p. m. -06:00 │
└─────────┴──────────────────────────────────┴──────────────────────────────────┘
```

To revert the last migration you can use the `down` command

```
$ migrondi down
[22:11:01 INF] Running '1' migrations.
[22:11:01 INF] Reverted migration 'create-users-table_1695873658869' successfully.
```

This will revert all of the migrations from the last applied to the first one into the database.

If we ran our `list` command again we would see that the migration has been reverted.

That's the General Gist of how to use migrondi.

## Command Reference

- `init`

  - aliases: setup
  - description: Creates a migrondi.json file where the comand is invoked or the path provided
  - arguments:
    - path: The path where the migrondi.json file will be created

- `new`

  - aliases: create
  - description: This will create a new SQL migration file in the configured directory for migrations
  - arguments:
    - name: The name of the migration file

- `up`

  - aliases: apply
  - options:
    - --dry, -d: Whether to run the migrations or just show what would be run
  - description: Runs migrations against the configured database
  - arguments:
    - amount: The amount of migrations to run

- `down`

  - aliases: rollback
  - options:
    - --dry, -d: Whether to run the migrations or just show what would be run
  - description: Runs migrations against the configured database
  - arguments:
    - amount: The amount of migrations to run

- `list`

  - aliases: show
  - description: Reads migrations files and the database to show what is the current state of the migrations
  - arguments:
    - migration kind: The kind of migrations to show which can be omited, can be either "up", or "down"

- `status`

  - aliases: show-state
  - description: Checks whether the migration file has been applied or not to the database
  - arguments:
    - name: The name of the migration file
