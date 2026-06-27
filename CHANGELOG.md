# Changelog

## [Unreleased]

## [1.3.0] - 2026-06-27

We've found some general isues that warrant the release of 1.3.0, the UI version will be in preview for the forseable future even if marked as a stable release, a notice was added to the startup fo the UI version

### Added

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
