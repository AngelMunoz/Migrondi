---
title: Use as a library (F#/VB/C#)
category: Overview
categoryindex: 2
---

From v1 and onwards Migrondi was built to be used as a library. This means that you can use Migrondi to run your own migrations from F# or C# code and even extend Migrondi functionality with your own storage providers.

## Terminology

I try to be consistent with the terminology used in the library, in case you find something out of place please let me know.

- **Migration Source**: This is where your migrations are stored. While Migrondi provides a default local implementation, you can provide your own by implementing the `IMiMigrationSource` interface. This allows you to store migrations in remote locations like S3, Azure Blob, or via a Web API.

- **Migration**: A representation of a migration "file" or source. It contains the actual SQL statements for both `Up` (apply) and `Down` (rollback) operations.

- **Migration Record**: A record stored in the database that tracks which migrations have been applied. It only contains metadata (name, timestamp) to identify the migration.

- **Database**: The target of all Migrondi operations, specified via the connection string in your configuration.

- **Up**: The operation to apply a migration to the database.

- **Down**: The operation to revert a migration from the database.

- **Pending**:
  - When talking about an `UP` operation, pending means that the migration has not been applied to the database.
  - When talking about a `DOWN` operation, pending means that the migration has been applied to the database (it's "pending" for rollback).

## URI Handling

Given that Migrondi.Core is designed to be used as a library, it's important to remark that internally all of the paths are expected to be URIs as that allows to work with both the local filesystem and URLs in case that's required by a remote file system of some kind implemented by you.

With that in mind I'd like yo remind you that

- `Uri("/path/to/file", UriKind.Absolute)` is not the same as `Uri("/path/to/file/", UriKind.Absolute)`.

The first one is a file, the second one is a directory. and the same happens for relative URIs

- `Uri("path/to/file", UriKind.Relative)` is not the same as `Uri("path/to/file/", UriKind.Relative)`.

This is important for the built-in file system service factory that requires both the `projectRootUri` and the `migrationsRootUri` to be directories.

### Public API Guidelines

The public API is designed to be as simple as possible, and to be used as a library, so it's important to remark that the public API should not contain any Language Specific Type (e.g. in the case of F# it should not contain `FSharpList<'T>` or `FSharpOption<'T>`) in any of it's signatures. while this might make it clunky to work with in F# it makes it easier to use in C# and VB.

## Usage

First, get it from NuGet

> `dotnet add package Migrondi.Core`

The main entry point for the library is the `IMigrondi` interface, created via the `MigrondiFactory`.

```fsharp
open Migrondi.Core

let config = MigrondiConfig.Default
let migrondi = Migrondi.MigrondiFactory(config, ".")
```

The service provides functionality to run migrations and related operations:

- `RunUp` / `RunUpAsync`
- `RunDown` / `RunDownAsync`
- `DryRunUp` / `DryRunUpAsync`
- `DryRunDown` / `DryRunDownAsync`
- `MigrationsList` / `MigrationsListAsync`
- `ScriptStatus` / `ScriptStatusAsync`

For more detailed information, check out:

- [Main Service API (IMigrondi)](./services/migrondi.md)
- [Custom Migration Sources (IMiMigrationSource)](./services/filesystem.fsx)
- [F# Scripts Examples](./examples/fsharp.fsx)
