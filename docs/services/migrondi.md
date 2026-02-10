---
title: Migrondi Service
category: Core
categoryindex: 3
index: 4
---

The `IMigrondi` interface is the main entry point for the Migrondi library. It coordinates between your migration source and the database to provide a simple API for managing migrations.

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

// Create service with default local file system source
let migrondi = Migrondi.MigrondiFactory(config, ".")

// Optionally, provide a custom logger
open Microsoft.Extensions.Logging
let logger = LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
                    .CreateLogger<IMigrondi>()
let migrondi = Migrondi.MigrondiFactory(config, ".", logger = logger)
```

## Custom Migration Sources

You can provide a custom `IMiMigrationSource` implementation to use non-local storage. This is done via the `source` parameter:

```fsharp
open Migrondi.Core.FileSystem

let customSource =
  { new IMiMigrationSource with
      member _.ReadContent(uri) = "..."
      member _.ReadContentAsync(uri, ct) = task { return "..." }
      member _.WriteContent(uri, content) = ()
      member _.WriteContentAsync(uri, content, ct) = task { () }
      member _.ListFiles(locationUri) = Seq.empty
      member _.ListFilesAsync(locationUri, ct) = task { return Seq.empty }
  }

let migrondi = Migrondi.MigrondiFactory(
  config,
  ".",
  ?logger = Some logger,
  source = customSource
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

### Manual Transactions

By default, Migrondi wraps each migration in a transaction. If you need to manage your own transactions (e.g., for `CREATE INDEX CONCURRENTLY` or multi-step processes), set `manualTransaction` to `true`:

```fsharp
// Create migration without automatic transaction handling
let migration = migrondi.RunNew(
  "create-index-concurrently",
  upContent = "CREATE INDEX CONCURRENTLY idx_users_email ON users(email);",
  downContent = "DROP INDEX CONCURRENTLY IF EXISTS idx_users_email;",
  manualTransaction = true
)
```

When `manualTransaction` is `true`, Migrondi will execute the SQL directly without an enclosing transaction. You are responsible for managing any transaction boundaries in your SQL.

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

### Migration Format Versions

Migrondi supports two migration file formats and automatically detects which format your migrations use:

**V1 Format (current)**:

- Filename: `{timestamp}_{name}.sql`
- Required headers: `-- MIGRONDI:NAME={name}` and `-- MIGRONDI:TIMESTAMP={timestamp}`
- Optional header: `-- MIGRONDI:ManualTransaction=true`

**V0 Format (deprecated)**:

- Filename: `{name}_{timestamp}.sql`
- Content marker: `-- ---------- MIGRONDI:UP:{timestamp} ----------`

The library handles both formats transparently - you don't need to manually migrate old files. New migrations created programmatically will use the V1 format.
