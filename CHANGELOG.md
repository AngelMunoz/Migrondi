# Changelog

## [Unreleased]

### Added

- **Migrondi:** The `mcp` server's tools are now stateless — every tool accepts a `configPath` argument to target any project from one registration (send `null` to use the startup binding), and two new tools join the surface: `init_project` creates a project in a directory (never deleting files) and `update_project_config` rewrites `migrondi.json` fields in place. `create_migration` also accepts `manualTransaction` for statements that cannot run inside a transaction.
- **Migrondi:** New `mcp` command serves one project over stdio as an MCP server, so agents and harnesses can drive migrations as tools (`get_project`, `list_migrations`, dry runs, `create_migration`, `run_migrations`, `run_rollback`) instead of shell commands. `--config` selects the project, anchors its directory as the project root, and layers `MIGRONDI_*` variables and the config override flags on top; `--readonly` serves only the read tools. All server logs go to stderr, keeping stdout clean for strict MCP clients.
- **Migrondi:** `new` accepts `--up`/`--down` inline SQL and `--up-file`/`--down-file` to read a section from a file (or stdin with `-`), so a migration can be created with its content in one call instead of creating an empty file and editing it afterwards.
- **Migrondi:** `list` accepts `--pending`/`--applied`, `--limit`/`--last`, `--search <text>`, and `--show-path`/`--show-content` to filter and inspect migrations without scanning the migrations folder by hand.
- **Migrondi:** `up` and `down` accept `--to <name>` to apply up to, or roll back to, a named migration (name, filename, or timestamp) instead of counting migrations; the positional amount stays optional and cannot be combined with `--to`.
- **Project:** Added `migrondi-cli` and `migrondi-library` agent skills under `.agents/skills`, covering CLI usage for agents and `Migrondi.Core` embedding patterns such as custom migration sources.

### Changed

- **Migrondi:** **Breaking (behavioral):** the MCP server re-reads the project config on every tool call, so config edits and `init_project` take effect on the next call without restarting the server, and `get_project` reflects the per-call project. Tool calls that omit optional arguments must now send them as explicit `null` (the schemas list them as required-but-nullable).
- **Migrondi.Mcp:** The shared MCP layer now reports full tool annotations (`idempotentHint` on read tools, `openWorldHint: false` everywhere), the real package version in the `initialize` response instead of a hardcoded value, and serves HTTP statelessly — no session store, localhost-only `Origin` acceptance, and no redundant `Mcp-Session-Id` headers.
- **Migrondi.Mcp:** MCP clients see the real parameter names (`amount`, `projectId`, ...) in tool schemas again instead of `delegateArg0`, which made those arguments impossible to pass; send `null` for an omitted "all migrations" value (e.g. `{"amount": null}`).

- **Project:** Migrated all projects to central package management via `src/Directory.Packages.props`, removing per-project package versions and consolidating dependency versions in one place.
- **Project:** Switched to the Microsoft Testing Platform (`global.json`) and bumped test-related packages to their latest stable versions.
- **MigrondiUI:** Upgraded from Avalonia 11 to Avalonia 12 and updated UI code to match the new API surface (`Watermark` → `PlaceholderText`, `IBinding` → `BindingBase`, nullable `TextBox.Text` handling, and `JsonElement` deserialization).
- **MigrondiUI:** Updated Avalonia-related dependencies (AvaloniaEdit, TextMate, SukiUI, NXUI) to their Avalonia 12-compatible versions.
- **Migrondi.Tests:** Migrated to `MSTest.Sdk/4.2.3`.
- **MigrondiUI.Tests:** Migrated to the Microsoft Testing Platform with the xUnit v3 runner, targeting `net10.0` only.
- **Project:** Updated all package dependencies to their latest stable versions, including Avalonia 12.1.6, ModelContextProtocol 2.4.0, MSTest 4.5.2, Serilog 4.4.0, and the database drivers, and updated the local Fantomas and FsDocs tools.
- **Project:** Moved the MCP tooling into a new shared `Migrondi.Mcp` project that holds the tool definitions, service contracts, result encoders, and both transports. MigrondiUI now keeps only the host code that builds its project collection and starts the server, so the CLI can host the same MCP tools against a single project in a later phase.

### Fixed

- **Migrondi.Mcp:** Every tool that reports a migration (`list_migrations`, both dry runs, `run_migrations`, `run_rollback`, `get_migration`) now reports the same real file name (e.g. `1794436380501_add_users.sql`). Previously these results recomposed the timestamp onto the decoded name — which already carries it for standard-written files — producing doubled names like `1794436380501_1794436380501_add_users` that match no file on disk and broke handing identifiers to the CLI (`status`, `--to`). For virtual projects the file name also lacked the `.sql` suffix.
- **Migrondi.Mcp:** The `create_migration` tool result now reports the migration's name (e.g. `add_users`) and its real file name (e.g. `1794436380501_add_users.sql`). Previously the file name was reported as the name and the timestamp was recomposed onto it, producing a doubled file name like `1794436380501_1794436380501_add_users.sql`; for virtual projects the file name also lacked the `.sql` suffix.
- **Migrondi:** `status` now returns a non-zero exit code when no migration matches the given name, instead of reporting `NotFound` with a success exit code.
- **Project:** Addressed the high-severity `SQLitePCLRaw.lib.e_sqlite3` vulnerability (GHSA-2m69-gcr7-jv3q) by overriding `SQLitePCLRaw.bundle_e_sqlite3` to `3.0.5` in central package management.
- **Project:** Addressed the high-severity `System.Security.Cryptography.Pkcs` vulnerabilities (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) that Microsoft.Data.SqlClient pulled in, by overriding that package to `10.0.14` in central package management. No project reports a vulnerable package now.

## [1.3.0] - 2026-06-27

### Added

- We've found some general isues that warrant the release of 1.3.0, the UI version will be in preview for the forseable future even if marked as a stable release, a notice was added to the startup fo the UI version

- **MigrondiUI:** Logs a preview notice at startup — MigrondiUI is preview software (regardless of whether a release is marked stable, beta, or rc), expect breaking changes before it stabilizes at 2.0.

### Changed

- **Migrondi:** **Breaking (behavioral):** `init` no longer silently overwrites an existing `migrondi.json` — it now stops with an error so an existing project isn't clobbered. `--force` overwrites with the default configuration; `--force --merge` adopts the values already in the file instead (recovering known fields even from a partial or otherwise-invalid JSON), and `--merge` requires `--force`.

### Fixed

- **Migrondi.Core:** Apply/rollback reported the wrong numbers — the summary counted every pending/applied migration rather than the ones actually being processed, and applying nothing still returned the full migration history (so re-running `up` logged every already-applied migration as newly applied, and the MCP apply result over-counted). Counts and results now reflect only the migrations processed in that run.
- **Migrondi:** `status` logged nothing for a valid migration and crashed when the name lacked a `.sql` suffix. It now reports `Applied` or `Pending` for a plain migration name (and a clean `NotFound` for an unknown one) in both human and JSON output.
- **Migrondi:** JSON output for `list` (`[mi-json]`) embedded each migration as an escaped JSON string, forcing a second decode pass; migrations are now emitted as native JSON objects.

## [1.3.0-beta-001] - 2026-06-26

### Added

- MigrondiUI: Desktop GUI application for managing multiple Migrondi projects
- MigrondiUI: Virtual project support - create projects without physical files
- MigrondiUI: Local project import and visualization
- MigrondiUI: Migration execution from GUI
- MigrondiUI: MCP (Model Context Protocol) server mode for AI assistant integration
- MigrondiUI: MCP tools now return typed domain results (`Result<'T, 'E>`) instead of raw `CallToolResult`
- MigrationName module with validation for migration names
- ResultExtensions for C# interop with F# Result type
- `global.json` pinning SDK to 10.0.301

### Changed

- MigrondiUI references Migrondi.Core by project reference rather than NuGet
- Updated Microsoft.Extensions.Logging.Console from 9.0.6 to 10.0.3
- Target frameworks: Library (Migrondi.Core, Migrondi.Tests) targets net8.0 + net10.0 LTS; apps (Migrondi CLI, MigrondiUI) also target net9.0 until November 2026
- Upgraded ModelContextProtocol from 0.8.0-preview.1 to 1.4.0
- Aligned Serilog.Extensions.Logging to 10.0.0 and Serilog.Sinks.Console to 6.1.1 in MigrondiUI
- Simplified CI workflow to a single build+test job
- Removed dead `MigrondiExt.fs` — superseded by `MigrationOperationsFactory` in Services.fs
- **Project:** Updated FSharp.SystemCommandLine from 2.0.0-beta5.3 to the stable 2.1.0 release

### Fixed

- **Migrondi.Core:** Applying or rolling back migrations against SQL Server failed. The migrations history table is now created with an auto-incrementing id, without the `GO` statement (invalid over the data provider), and without a hard-coded `dbo` schema, so it respects the connection's default schema. The async "last applied" lookup also uses SQL Server's `TOP 1` rather than the unsupported `LIMIT 1`.
- **Migrondi:** Commands that don't touch the database (`new`, `init`, `--help`) crashed at startup whenever the configured database was unreachable or used a non-SQLite driver. Database setup now runs only for the commands that actually need it.
- MCP `StructuredContent` type compatibility with ModelContextProtocol 1.4.0 (`JsonNode` → `Nullable<JsonElement>`)
- CI workflow attempting to build Migrondi.Core for net9.0 (target no longer exists)

## [1.2.0] - 2026-02-11

### Fixed

- Make sure that custom source resolution get the expected uris and not resolved file paths

## [1.1.0] - 2026-02-11

### Added

- Expose serialization to avoid userland duplication

## [1.0.1] - 2026-02-10

### Changed

- Migrondi v1.0.0 release
- Remove RepoDB as a dependency
- Add migration-source-abstractions
