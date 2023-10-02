---
title: Use as a library (F#/VB/C#)
category: Overview
categoryindex: 2
---

From v1 and onwards Migrondi was built to be used as a library. This means that you can use Migrondi to run your own migrations from F# or C# code. and perhaps even extend Migrondi functionality. with your own.

## Terminology

I try to be consistent with the terminology used in the library, in case you find something out of place please let me know.

- FileSystem: the file system is where migrations are stored locally, it does not necessarily mean the local file system, it could be a remote file system as well in case of custom implementations.

  - Source or sources - source in the context of the file system represents a location that contains text, usually these would be called files, but since the file system is not necessarily local, I prefer to call them sources.

- Database: The database is the one specified with the connection string in the configuration object, and is the target of all of the migrondi operations.

  - Source or sources: When we're performing reading operations, source in the context of the database represents a migration already applied to the database. If we're performing writing operations, then the source is a `Migration` object that is going to be applied to the database not a file from the file system.

- Up: Up is often referred as the operation to apply a migration to the database, this is the operation that will be performed when running `migrondi up` from the command line.

- Down: Down is often referred as the operation to revert a migration from the database, this is the operation that will be performed when running `migrondi down` from the command line.

- Pending:
  - When we'retalking about an `UP` operation, pending means that the migration has not been applied to the database.
  - When we're talking about a `DOWN` operation, pending means that the migration has been applied to the database.

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

After that most of the core is under it's own namespage e.g.

- `Migrondi.Core.Database`
- `Migrondi.Core.Migrondi`

The ideal is to build up a `MigrondiService` instance which will work as the main interface for your code to handle the library.

The service is nothing too crazy it provides basic functionality to run migrations and related operations.

- RunUp
- RunDown
- DryRunUp
- DryRunDown
- MigrationsList
- ScriptStatus

The same service provides also an async version of the same methods

- RunUpAsync
- RunDownAsync
- DryRunUpAsync
- DryRunDownAsync
- MigrationsListAsync
- ScriptStatusAsync

While you could do this entirely on your own, there are some helper methods that you can use to get started, but for that you will need a couple of services as well.

- `MigrondiServiceFactory.GetInstance`

That is a factory method that will create a new instance of the `MigrondiService` class.
It requires a couple of services as well as a configuration object.

- `DatabaseService` - This service is in charge of handling the database connection and executing the SQL scripts.
- `FileSystemService` - This service is in charge of handling the file system and the migrations files.
- `ILogger` - a `Microsoft.Extensions.Logging.ILogger` compatible instance logger.
- `MigrondiConfig` - a configuration object that contains the configuration for the library.

What the `MigrondiService` does is to coordinate between both the database and whatever file system you're using to store the migration scripts.

We can talk more deeply about the existing services and how to use them in the next sections.

- [Serialization](./services/serialization.md)
- [FileSystemService](./services/filesystem.md)
- [DatabaseService](./services/database.md)
- [MigrondiService](./services/migrondi.md)
