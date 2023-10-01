---
title: Use as a library (F#/VB/C#)
category: Overview
categoryIndex: 2
---

From v1 and onwards Migrondi was built to be used as a library. This means that you can use Migrondi to run your own migrations from F# or C# code. and perhaps even extend Migrondi functionality. with your own.

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

- [Core Types](./types.md)
- [Serialization](./services/serialization.md)
- [FileSystemService](./services/filesystem.md)
- [DatabaseService](./services/database.md)
- [MigrondiService](./services/migrondi.md)
