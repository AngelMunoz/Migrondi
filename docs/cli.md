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

Alternatively, you can create a new migration with this header already present by using the `--manual-transaction` (or `-m`) flag:

```bash
$ migrondi new create-index-concurrently --manual-transaction
```

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
  - options:
    - --manual-transaction, -m: Signals migrondi to create a migration that will not be wrapped in a transaction when executed
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

## MCP Server

`migrondi mcp` runs an MCP (Model Context Protocol) server over stdio, so AI agents and harnesses can drive migrations as tools instead of shell commands.

The server is **stateless**: nothing about a project is cached between calls. Every tool call resolves its own project binding, builds its own database connection, and runs. One server registration can therefore drive any project on the machine by passing `configPath` per call — or serve one project per workspace when the registration pins `--config` and the tools run with `configPath: null`.

The server exposes nine tools:

- `get_project` — reports the project name, config path, driver, migrations folder, and table name (never the connection string)
- `list_migrations` — lists migrations with their applied/pending status
- `dry_run_migrations` — previews the pending migrations that would be applied
- `dry_run_rollback` — previews the applied migrations that would be reverted
- `create_migration` — creates a new migration file, optionally with `upContent` and `downContent` SQL, and optionally with `manualTransaction: true` for statements that cannot run inside a transaction (e.g. `CREATE INDEX CONCURRENTLY`)
- `run_migrations` — applies pending migrations (optionally a count)
- `run_rollback` — reverts applied migrations (optionally a count)
- `init_project` — creates a project in a directory: writes `migrondi.json` and creates the migrations folder; an empty directory gets the defaults, a non-empty directory with `force: true` gets its config fields recovered and rewritten (files are never deleted)
- `update_project_config` — re-writes the project's `migrondi.json` with the given fields layered on top of its current values

Every project tool takes `configPath` as its first argument: pass a path to target that project, or `null` to use the server's startup binding.

### The explicit-`null` convention for optional arguments

Tool schemas mark every optional argument as required-but-nullable. To omit an optional argument, agents must send an explicit `null` for it — for example `{"configPath": null, "amount": null}` — not leave the key out. This applies to `configPath`, `amount`, `upContent`, `downContent`, `manualTransaction`, and every optional field of `init_project` and `update_project_config`. The SDK parameter binder rejects a missing key for these arguments.

### Configuration resolution

The startup `--config` is resolved once to fail fast on a bad value; every tool call then re-resolves its own binding in this order:

1. The call's `configPath` argument — when given, it wins. A missing or invalid file is a tool error.
2. `--config <path>` — the startup default. A missing or invalid file fails the server start with a non-zero exit. The config file's directory becomes the project root, so the migrations folder and relative SQLite paths resolve against that directory, not the server's working directory.
3. `cwd/migrondi.json` — the last fallback, with the same defaults the other commands use. A missing file starts the server (the fallback happens per call).

The config file is re-read on every call: editing `migrondi.json` (or running `init_project`) takes effect on the next call without restarting the server, and `get_project` always reports the project it resolved for that call.

`MIGRONDI_*` environment variables — `MIGRONDI_CONNECTION_STRING`, `MIGRONDI_MIGRATIONS`, `MIGRONDI_TABLE_NAME`, `MIGRONDI_DRIVER` — override the resolved config file's values, and the config override flags (`--connection/-c`, `--migrations/-m`, `--table-name/-t`, `--driver/-d`) beat the environment variables, matching the rest of the CLI.

`--readonly` serves only the read tools (`get_project`, `list_migrations`, `dry_run_migrations`, `dry_run_rollback`). `--stdio` is the default (and currently the only) transport; `--http` is rejected.

All server logs go to stderr, so stdout carries only MCP protocol messages and the server is safe to plug into strict clients.

### Harness configuration example

Point the harness at an absolute config path; no working directory is needed:

```json
{
  "mcpServers": {
    "migrondi": {
      "command": "migrondi",
      "args": ["mcp", "--config", "/absolute/path/to/migrondi.json"]
    }
  }
}
```

A single global registration (no `--config`, started from any directory) also works: every tool call carries the `configPath` of the project it targets, and `configPath: null` falls back to the server's working directory project.

