---
title: Migrondi Service
category: Core
categoryindex: 3
index: 4
---

The `IMigrondi` interface is the main entry point for the Migrondi library. It coordinates between the file system and database services to provide a simple API for managing migrations.

## Creating a Migrondi Service

Use the `MigrondiFactory` to create a new service instance:

```fsharp
open Migrondi.Core

let config = {
  MigrondiConfig.Default with
    connection = "Data Source=./migrondi.db"
    migrations = "./migrations"
    driver = MigrondiDriver.Sqlite
}

// Create service with default file system
let migrondi = Migrondi.MigrondiFactory(config, ".")

// Optionally, provide a custom logger
open Microsoft.Extensions.Logging
let logger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
                    .CreateLogger<IMigrondi>()
let migrondi = Migrondi.MigrondiFactory(config, ".", ?logger = Some logger)
```

## Custom File System (Experimental)

> **Experimental:** This API may change in future versions.

You can provide a custom `IMiFileSystem` implementation to use non-local storage:

```fsharp
open Migrondi.Core.FileSystem

let customFileSystem = MyCustomFileSystem()

let migrondi = Migrondi.MigrondiFactory(
  config,
  ".",
  ?logger = Some logger,
  ?miFileSystem = Some customFileSystem
)
```

This allows you to store migrations in:
- Cloud storage (S3, Azure Blob, etc.)
- Virtual file systems
- Version control systems
- Databases as file storage

## Initialization

Before using the database, you must initialize it:

```fsharp
// Synchronous
migrondi.Initialize()

// Asynchronous
migrondi.InitializeAsync() |> Async.AwaitTask
```

This creates the migrations tracking table if it doesn't exist. It's safe to call multiple times - it won't recreate an existing table.

## Creating Migrations

Create new migration files with `RunNew`:

```fsharp
// Synchronous
let migration = migrondi.RunNew(
  "create-users-table",
  upContent = "CREATE TABLE users (id INT, name VARCHAR(255));",
  downContent = "DROP TABLE users;"
)

// Asynchronous
let! migration = migrondi.RunNewAsync(
  "create-users-table",
  upContent = "CREATE TABLE users (id INT, name VARCHAR(255));",
  downContent = "DROP TABLE users;"
)
```

If you omit `upContent` and `downContent`, default templates will be used:

```fsharp
let migration = migrondi.RunNew("create-users-table")
```

## Listing Migrations

Get the status of all migrations:

```fsharp
// Synchronous
let migrations = migrondi.MigrationsList()

// Asynchronous
let! migrations = migrondi.MigrationsListAsync()
```

Returns a `MigrationStatus IReadOnlyList` where each item is either:
- `Applied migration` - Migration has been applied to database
- `Pending migration` - Migration is pending application

**Example:**

```fsharp
for migration in migrations do
  match migration with
  | Applied m ->
      printfn "Applied: %s" m.name
  | Pending m ->
      printfn "Pending: %s" m.name
```

## Applying Migrations

Apply pending migrations with `RunUp`:

```fsharp
// Apply all pending migrations
migrondi.RunUp()

// Apply specific number of migrations
migrondi.RunUp(amount = 3)

// Asynchronous
migrondi.RunUpAsync() |> Async.AwaitTask
migrondi.RunUpAsync(amount = 3) |> Async.AwaitTask
```

Returns a `MigrationRecord IReadOnlyList` of migrations that were applied.

### Dry Run

Preview migrations without applying them:

```fsharp
let pending = migrondi.DryRunUp()

for migration in pending do
  printfn "Would apply: %s" migration.name
  printfn "SQL: %s" migration.upContent
```

## Rolling Back Migrations

Revert applied migrations with `RunDown`:

```fsharp
// Rollback last migration
migrondi.RunDown()

// Rollback specific number of migrations
migrondi.RunDown(amount = 2)

// Asynchronous
migrondi.RunDownAsync() |> Async.AwaitTask
```

Returns a `MigrationRecord IReadOnlyList` of migrations that were rolled back.

### Dry Run

Preview migrations without rolling them back:

```fsharp
let toRollback = migrondi.DryRunDown()

for migration in toRollback do
  printfn "Would rollback: %s" migration.name
  printfn "SQL: %s" migration.downContent
```

## Checking Migration Status

Check if a specific migration has been applied:

```fsharp
let status = migrondi.ScriptStatus("1708216610033_create-users-table.sql")

match status with
| Applied migration ->
    printfn "Migration applied: %s" migration.name
| Pending migration ->
    printfn "Migration pending: %s" migration.name
```

## Error Handling

Migrondi methods throw specific exceptions:

- **`SetupDatabaseFailed`**: Database initialization failed
- **`MigrationApplicationFailed`**: Migration failed to apply (transaction rolled back)
- **`MigrationRollbackFailed`**: Migration failed to rollback (transaction rolled back)
- **`SourceNotFound`**: Migration file not found
- **`DeserializationFailed`**: Could not deserialize migration file
- **`MalformedSource`**: Migration file is malformed

**Example:**

```fsharp
try
  migrondi.RunUp()
with
| :? MigrationApplicationFailed as ex ->
  printfn "Failed to apply migration: %s" ex.Message
  // Migration was rolled back automatically
| :? SourceNotFound as ex ->
  printfn "Migration not found: %s" ex.Message
| ex ->
  printfn "Unexpected error: %s" ex.Message
```

## Complete Workflow Example

```fsharp
open Migrondi.Core
open Microsoft.Extensions.Logging

// 1. Create configuration
let config = {
  MigrondiConfig.Default with
    connection = "Data Source=./myapp.db"
    migrations = "./migrations"
}

// 2. Create logger
let logger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
                .CreateLogger<IMigrondi>()

// 3. Create service
let migrondi = Migrondi.MigrondiFactory(config, ".", ?logger = Some logger)

// 4. Initialize database
migrondi.Initialize()

// 5. List current status
let migrations = migrondi.MigrationsList()
printfn "Current migrations: %d" migrations.Count

// 6. Dry run to see what would apply
let pending = migrondi.DryRunUp()
printfn "Pending migrations: %d" pending.Count

// 7. Apply migrations
let applied = migrondi.RunUp()
printfn "Applied %d migrations" applied.Count
```

## Async API Reference

All methods have async equivalents:
- `InitializeAsync`
- `RunNewAsync`
- `RunUpAsync`
- `RunDownAsync`
- `DryRunUpAsync`
- `DryRunDownAsync`
- `MigrationsListAsync`
- `ScriptStatusAsync`

All async methods support optional `CancellationToken`:

```fsharp
open System.Threading

let cancellationToken = new CancellationTokenSource(5000).Token
let! migrations = migrondi.MigrationsListAsync(cancellationToken)
```

## Migration Ordering

Migrations are ordered by timestamp, not filename. The V1 format (`{timestamp}_{name}.sql`) ensures proper ordering:

- Newest migrations (highest timestamp) are applied first
- Oldest migrations are rolled back first
- Timestamps are in Unix milliseconds

When creating migrations with `RunNew`, the current time is used for the timestamp automatically.
