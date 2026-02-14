# Changelog

## [Unreleased]

### Added

- MigrondiUI: Desktop GUI application for managing multiple Migrondi projects
- MigrondiUI: Virtual project support - create projects without physical files
- MigrondiUI: Local project import and visualization
- MigrondiUI: Migration execution from GUI
- MigrondiUI: MCP (Model Context Protocol) server mode for AI assistant integration
- MigrationName module with validation for migration names
- ResultExtensions for C# interop with F# Result type

### Changed

- MigrondiUI references Migrondi.Core by project reference rather than NuGet
- Updated Microsoft.Extensions.Logging.Console from 9.0.6 to 10.0.3

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
