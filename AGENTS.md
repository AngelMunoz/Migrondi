# AGENTS.md

Instructions for agentic coding assistants working on the Migrondi codebase.

## Project Overview

Migrondi is a SQL migrations tool: you write versioned `.sql` files and it tracks which have been applied to a database — applying pending ones ("up") or rolling applied ones back ("down"), plus dry-run, list, and per-file status checks. It runs each migration inside a transaction (with an opt-out `manualTransaction` flag for statements like `CREATE INDEX CONCURRENTLY`) and supports **SQLite, SQL Server (MSSQL), PostgreSQL, and MySQL** over ADO.NET (see the `MigrondiDriver` DU).

The repo ships three independently useful components built on one engine:

- **`Migrondi.Core`** — the packable migration library. The `IMigrondi` service (built via `Migrondi.MigrondiFactory`) coordinates the file system and database: create migrations, apply/rollback (optionally a count), dry-run, and list status. Migration content is read through a pluggable `IMiMigrationSource` — the default reads SQL from disk, but you can supply a custom source (HTTP, S3, blob storage, …). The public API is CLS-friendly (sync + async overloads, `Result` extensions), so it embeds cleanly into F#/C#/VB applications.
- **`Migrondi`** — the CLI, distributed as a `dotnet` global tool (`migrondi`) and as self-contained single-file binaries for Windows/macOS/Linux (x64 + arm64). Commands: `init`, `new`, `up`/`apply`, `down`/`rollback`, `list`/`show`, `status` (each with aliases).
- **`MigrondiUI`** — an Avalonia desktop app that also runs as an **MCP (Model Context Protocol) server**. As a UI it manages multiple migration "projects" (on-disk local projects and virtual projects stored in its own database). In MCP mode it exposes those projects and migration operations (create/update/delete migrations, dry-run, apply, rollback, import/export) as tools, so an AI agent can drive migrations over MCP (MCP operations target virtual projects).

The codebase uses F# with `.fsi` signature files, MSTest for tests, and Fantomas for formatting.

## Imperatives

1. **NEVER PUSH WITHOUT PERMISSION.** Always ask before pushing to the remote.
2. **NEVER FORCE PUSH.** Tell the user they have to force push instead of you.
3. **Always run `dotnet fantomas .` before committing code.** Format all F# files before staging.
4. **Never use `Option.get` or `ValueOption.get`.** Always pattern match (`match`, `function`, `if ... then`) or use safe alternatives (`Option.defaultValue`, `Option.map`, `Array.choose`, etc.) to handle option values. Unchecked `.get` calls crash at runtime on `None`.
5. Pull requests made with the `gh` command should use a markdown file as the PR body, not inline escaped markdown strings.

## Project Structure

- `src/Migrondi.Core` — the packable migration library/engine.
- `src/Migrondi` — the CLI (global tool + self-contained binaries).
- `src/MigrondiUI` — the Avalonia app and MCP server.
- `src/Migrondi.Tests`, `src/MigrondiUI.Tests` — test projects (not packable).
- `samples/` — sample usage.
- `docs/` — the [FsDocs](https://fsprojects.github.io/FSharp.Formatting/) documentation site.

> Target frameworks, package versions, and similar build details live in each project's `.fsproj` and `Directory.Build.props` — read those directly rather than relying on this file, since they change over time.

## Migration File Format

Migration `.sql` files are **parsed by strict regex**, not free-form text. The `-- MIGRONDI:` lines and the `UP`/`DOWN` delimiters carry data — never delete, reorder, reformat, or "tidy" them (the files themselves say `-- Do not remove MIGRONDI comments.`). The authoritative parser is `src/Migrondi.Core/Serialization.fs` (`Migration.EncodeText` / `Migration.DecodeText`); filename matching lives in `Library.fsi` (`Matcher.V0` / `Matcher.V1`). When in doubt, read those.

### V1 format — current; used for all new migrations

```sql
-- MIGRONDI:NAME=add_users
-- MIGRONDI:TIMESTAMP=1586550686936
-- MIGRONDI:ManualTransaction=true
-- ---------- MIGRONDI:UP ----------
CREATE TABLE users (id INTEGER PRIMARY KEY);
-- ---------- MIGRONDI:DOWN ----------
DROP TABLE users;
```

- Metadata lines are `-- MIGRONDI:KEY=VALUE`; both `KEY` and `VALUE` may only contain `[a-zA-Z0-9_-]+`.
  - `NAME` and `TIMESTAMP` (an `int64`) are required.
  - `ManualTransaction=true|false` is optional — emitted only when `true`; absent or any non-`true` value means `false`.
- Section delimiters must be **exactly** `-- ---------- MIGRONDI:UP ----------` and `-- ---------- MIGRONDI:DOWN ----------` (mind the dashes). "Up" content is everything between the two delimiters; "Down" (rollback) content is everything after the DOWN delimiter. Both are trimmed.
- Set `ManualTransaction=true` only for statements that can't run inside a transaction (e.g. `CREATE INDEX CONCURRENTLY`); otherwise omit it so the migration runs in a transaction and rolls back on failure.

### V0 format — pre-v1, deprecated (read-only)

```sql
-- ---------- MIGRONDI:UP:1586550686936 ----------
CREATE TABLE users (id INTEGER PRIMARY KEY);
-- ---------- MIGRONDI:DOWN:1586550686936 ----------
DROP TABLE users;
```

- The timestamp is embedded in the delimiter (`MIGRONDI:UP:<ts>` / `MIGRONDI:DOWN:<ts>`); the name comes from the **filename**, and `manualTransaction` is always `false`.
- A file is decoded as V0 if it contains a `-- ---------- MIGRONDI:UP|DOWN:<digits> ----------` line. **Never create V0 files** — the parser only reads legacy ones for backwards compatibility.

### Filenames

- **V1 (current):** `<timestamp>_<name>.sql` — e.g. `1586550686936_add_users.sql`.
- **V0 (legacy):** `<name>_<timestamp>.sql`. Both are still recognized when migrations are listed.
- Extension must be `.sql` or `.SQL`; `name` matches `[a-zA-Z0-9_-]+` (enforced by `MigrationName.Validate`).
- Prefer creating migrations through the CLI (`migrondi new`), the UI, or the MCP tools — they generate the correct filename and V1 body. If you hand-author one, match V1 exactly.

### `migrondi.json` (project config)

Also parsed (JSON, via Thoth): keys `connection`, `migrations` (the migrations directory), `driver`, and optional `tableName` (defaults to `__migrondi_migrations`). `driver` accepts `sqlite`, `mssql`/`sqlserver`, `postgres`/`postgressql`, `mysql`/`mariadb`. Keep it valid JSON with those keys.

## Build and Test Commands

### Build

```bash
# Full build with FsMake
dotnet build

# Direct dotnet build
dotnet build src/Migrondi/Migrondi.fsproj
dotnet build src/Migrondi.Core/Migrondi.Core.fsproj

# Build for specific runtime
dotnet fsi build.fsx build:runtime -- linux-x64
```

### Test

```bash
# Run all tests
dotnet test src/Migrondi.Tests

# Run single test
dotnet test src/Migrondi.Tests --filter "FullyQualifiedName=Namespace.ClassName.MethodName"

# Run tests for specific framework
dotnet test src/Migrondi.Tests -f net8.0 --no-restore
```

### Format and Lint

```bash
dotnet fantomas . # or relative path to the file
```

### Run CLI

```bash
dotnet run --project src/Migrondi -- <command>
```

### Public API Guidelines

**Critical:** No F#-specific types for C#/VB interop (use `seq` not `list`, `T option` not `'T option`). Document with XML comments. Provide sync and async versions.

### Signature Files (.fsi)

Core library uses .fsi files before .fs in project order. Include full type signatures and XML docs.
When using signature files, by default all code is private to the module, only what is declared in the .fsi file is exposed.
you can still declare private or internal in the fsi file to expose things for testing but it is not encouraged.

## CI/CD

GitHub Actions live in `.github/workflows/`:

- `dotnetcore.yml` — restores, builds, and tests `Migrondi.slnx` on .NET 10, on `main`/`vnext` pushes and PRs.
- `docs.yml` — builds the FsDocs site and deploys it to GitHub Pages (from `vnext`).
- `release.yml` — a `workflow_dispatch` release: extracts the version from `CHANGELOG.md`, runs tests, packs the NuGet packages, publishes self-contained CLI/UI binaries for `linux`/`osx`/`win` (x64/arm64), and creates a GitHub Release.

## Changelog Management

We follow https://github.com/ionide/KeepAChangelog guidelines.

Changelog format:

```markdown
# Changelog

## [Unreleased]

Content that is pending for release goes here.

## [1.0.0] - 2026-06-24

### Added

- Initial release
```

Each section may contain the following categories:

- Added
- Changed
- Deprecated
- Removed
- Fixed
- Security

When adding entries to the changelog, make sure to follow format and categories.

### Writing style

The changelog is written for **developers upgrading their version**, not as a development journal. Keep these rules in mind:

1. **Concise and reader-focused.** Each entry is one bullet that says what changed and why a user cares — not how it's implemented internally. No internal module/file paths, no build/milestone/phase numbers (e.g. "B12", "Phase 3"), no section references (e.g. "§6.2"), and no "mirrors the canonical X" narration. A reader should understand the entry without reading the code.

2. **Group by user-facing concern, not by task.** One bullet per feature/fix area. If multiple commits touch the same subsystem, collapse them into one bullet that names each fix briefly, rather than one bullet per commit.

3. **Only released code can be Changed or Fixed.** Features that have never shipped belong in `Added` — there is no prior version to change from or fix against. Design choices and implementation details of a new feature are part of its `Added` description, not separate `Fixed`/`Changed` entries. Use `Changed`/`Fixed` only for modifications to already-released behavior (and mark breakage with **Breaking:** or **Breaking (behavioral):**).

4. **Lead with the affected surface.** Bold-prefix each bullet with the area: `**Migrondi:**`, `**Migrondi.Core:**`, `**MigrondiUI:**`, `**CI:**`, `**Project:**`, etc. Keep breaking changes at the top of their category.

5. **Plain language.** Describe the user-visible effect, not the code diff. The reader wants to know what they'll observe, not what line changed.

### Versioning

The package version is derived from `CHANGELOG.md` at pack time by `Ionide.KeepAChangelog.Tasks` (wired in `Directory.Build.props`), and applies to every packable project. There is no need to edit versions in project files or CI manually. Cutting a release is done by moving the desired content from `[Unreleased]` into a new version section, then triggering the **Create Release** workflow.
