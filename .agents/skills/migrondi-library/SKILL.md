---
name: migrondi-library
description: Embed Migrondi.Core in an application to run SQL migrations.
---

# Migrondi Library

Use this skill when embedding `Migrondi.Core` in an application instead of driving the CLI.

## Setup

Install with `dotnet add package Migrondi.Core`.

## Main entry point

Create the service with `Migrondi.MigrondiFactory(config, rootDirectory, ?logger, ?migrationSource)`, which returns `IMigrondi`.

## IMigrondi surface

Every operation is available in sync and async pairs:

- `RunNew(name, ?upContent, ?downContent, ?manualTransaction)` returns a `Migration` and creates `<timestamp>_<name>.sql`.
- `RunUp(?amount)` / `RunDown(?amount)` apply or roll back migrations.
- `DryRunUp(?amount)` / `DryRunDown(?amount)` preview the migrations that would be applied or reverted.
- `MigrationsList()` returns `MigrationStatus` values of `Applied` or `Pending`.
- `ScriptStatus(filename)` returns the status of one migration and throws `SourceNotFound` when the file is missing.

The public API is CLS-friendly, so it embeds cleanly into F#/C#/VB applications.

## Custom migration sources

Implement `IMiMigrationSource` (`ReadContent`/`WriteContent`/`ListFiles` plus the `Async` variants; throw `SourceNotFound` when a resource is missing) and pass it as `migrationSource` to the factory. The library handles `MIGRONDI` serialization itself, so hand-authored migration text must match the V1 format exactly or go through `RunNew`/`Migration.EncodeText`.

- Do NOT implement the internal `IMiFileSystem` interface.
- URIs are paths: a directory URI needs a trailing slash.
