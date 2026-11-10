---
name: migrondi-cli
description: Drive the migrondi CLI to manage SQL migrations.
---

# Migrondi CLI

Use this skill when driving the `migrondi` CLI. Each command is single-purpose; you orchestrate the sequence yourself.

## Project model

- Project root is the invocation directory (cwd); the config is always `<cwd>/migrondi.json`.
- Config keys: `connection`, `migrations` (migrations directory), `driver`, optional `tableName` (defaults to `__migrondi_migrations`).
- Environment overrides: `MIGRONDI_CONNECTION_STRING`, `MIGRONDI_MIGRATIONS`, `MIGRONDI_TABLE_NAME`, `MIGRONDI_DRIVER`.
- The migrations folder and a relative SQLite path resolve against the invocation directory.

## Commands

### init [path] [--force] [--force --merge]

Alias: `setup`. Creates a `migrondi.json` and the migrations directory at the given path (or cwd).

### new <name> [--manual-transaction|-m] [--up <sql>] [--down <sql>] [--up-file <path>] [--down-file <path>]

Alias: `create`. Creates a migration file with the V1 format. `-` as a file path reads that section from stdin. Inline and file input for the same section are mutually exclusive and error. Omitting content keeps empty template sections.

### list [--kind|-k up|down] [--pending] [--applied] [--limit n] [--last n] [--search text] [--show-path] [--show-content]

Alias: `show`. `kind up` means applied, `kind down` means pending. Contradictory status flags error. `list` only lists; it changes nothing.

### up [amount] [--dry|-d] [--to <name>] / down [amount] [--dry|-d] [--to <name>]

Aliases: `apply` / `rollback`. `amount` is optional. `--to` is mutually exclusive with `amount` and accepts a friendly name, a filename with or without `.sql`, or a timestamp. `up --to` includes the target; `down --to` leaves the target applied. An unknown target errors.

### status <name>

Alias: `show-state`. Name is the timestamped filename as shown by `list`. Exit 0 for applied/pending, exit 1 when not found.

## Output tokens

`[mi-json]` emits compact JSON log lines (only `list` embeds data). `[mi-debug]` emits debug lines.

## V1 format rules

- Never edit, delete, reorder, or reformat the `-- MIGRONDI:` metadata lines or the exact UP/DOWN delimiter lines (`-- ---------- MIGRONDI:UP ----------` and `-- ---------- MIGRONDI:DOWN ----------`).
- Never rename an applied migration.
- Migration names match `[a-zA-Z0-9_-]+`.
- Set `--manual-transaction` only for statements that cannot run inside a transaction (e.g. `CREATE INDEX CONCURRENTLY`).

## Workflow

`init` -> `new` with content -> `list --pending` -> `up --dry` -> `up` (or `up --to`) -> `status` -> `down --dry` -> `down`.
