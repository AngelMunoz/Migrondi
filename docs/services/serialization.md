---
title: Serialization Services
category: Core
categoryindex: 3
index: 1
---

The serialization services handle reading and writing migration files and configuration. Migrondi supports two serialization formats and provides backward compatibility with earlier versions.

## Migration File Format

Migrondi migration files use a special text format with markers to separate the migration content from the rollback content.

### V1 Format (Current)

The current format uses the timestamp prefix for proper ordering:

```
{timestamp}_{name}.sql
```

Example: `1708216610033_initial-tables.sql`

**File Content:**

```sql
-- MIGRONDI:NAME=initial-tables_1708216610033.sql
-- MIGRONDI:TIMESTAMP=1708216610033
-- ---------- MIGRONDI:UP ----------
-- Add your SQL migration code below

-- ---------- MIGRONDI:DOWN ----------
-- Add your SQL rollback code below
```

The `-- MIGRONDI:NAME` and `-- MIGRONDI:TIMESTAMP` comments are required and must appear at the top of the file. The markers `-- ---------- MIGRONDI:UP ----------` and `-- ---------- MIGRONDI:DOWN ----------` separate the migration content from the rollback content.

### V0 Format (Deprecated, Read-Only)

The deprecated format used the name prefix:

```
{name}_{timestamp}.sql
```

Example: `initial-tables_1708216610033.sql`

Migrondi can still read and apply V0 format migrations for backward compatibility, but new migrations are always created in V1 format.

## Serialization Interface

The `IMiMigrationSerializer` interface provides methods for encoding and decoding migrations:

```fsharp
type IMiMigrationSerializer =

  abstract member EncodeJson: content: Migration -> string
  abstract member EncodeText: content: Migration -> string

  abstract member DecodeJson: content: string -> Migration
  abstract member DecodeText: content: string * ?migrationName: string -> Migration
```

### Text Serialization

Text serialization is used for migration files on disk:

```fsharp
let serializer = MigrondiSerializer()

let migration = {
  name = "add-users-table"
  timestamp = 1708216610033L
  upContent = "CREATE TABLE users (id INT, name VARCHAR(255));"
  downContent = "DROP TABLE users;"
  manualTransaction = false
}

// Encode to text format
let text = serializer.EncodeText(migration)

// Decode from text format
let decoded = serializer.DecodeText(text)
```

### JSON Serialization

JSON serialization is useful for API responses or custom storage:

```fsharp
// Encode to JSON
let json = serializer.EncodeJson(migration)

// Decode from JSON
let decoded = serializer.DecodeJson(json)
```

## Configuration Serialization

The `IMiConfigurationSerializer` handles reading and writing the `migrondi.json` configuration file:

```fsharp
type IMiConfigurationSerializer =

  abstract member Encode: content: MigrondiConfig -> string
  abstract member Decode: content: string -> MigrondiConfig
```

**Example Configuration (migrondi.json):**

```json
{
  "connection": "Data Source=./migrondi.db",
  "migrations": "./migrations",
  "tableName": "__migrondi_migrations",
  "driver": "sqlite"
}
```

## Manual Transactions

The `manualTransaction` flag in the `Migration` record controls how migrations are executed:

- **`manualTransaction = false` (default):** Migrondi wraps each migration in a transaction automatically. If the migration fails, the transaction is rolled back.

- **`manualTransaction = true`:** Migrondi does not wrap the migration in a transaction. You are responsible for transaction management within your SQL. This is useful for:
  - Migrations that need multiple transactions
  - Migrations that use database-specific transaction features
  - Migrations that need to commit intermediate results

**Example with Manual Transaction:**

```sql
-- MIGRONDI:NAME=multi-step_1708216610033.sql
-- MIGRONDI:TIMESTAMP=1708216610033
-- ---------- MIGRONDI:UP ----------
BEGIN TRANSACTION;

CREATE TABLE users (id INT PRIMARY KEY, name VARCHAR(255));

INSERT INTO users (id, name) VALUES (1, 'Admin');

COMMIT;

-- ---------- MIGRONDI:DOWN ----------
BEGIN TRANSACTION;

DROP TABLE users;

COMMIT;
```

> **Warning:** When using `manualTransaction = true`, you must ensure proper error handling and rollback logic in your SQL. Migrondi will not rollback the migration if it fails.

## Migration Records

The database stores migration records using the `MigrationRecord` type:

```fsharp
type MigrationRecord = {
  id: int64
  name: string
  timestamp: int64
}
```

Note that `MigrationRecord` does not store the SQL content - it only tracks which migrations have been applied and their order.

## Custom Serialization

You can provide custom serializers to the `MigrondiFactory`:

```fsharp
let customSerializer = MyCustomSerializer()

let migrondi = Migrondi.MigrondiFactory(
  config,
  ".",
  ?miFileSystem = Some customFileSystem
)
```

This allows you to:
- Store migrations in a cloud storage system
- Use a custom file format
- Add validation or transformation logic
- Support additional metadata

## Error Handling

Serialization operations can throw the following exceptions:

- **`DeserializationFailed`**: Thrown when content cannot be parsed
- **`MalformedSource`**: Thrown when required fields are missing
- **`SourceNotFound`**: Thrown when a file cannot be found

Always handle these exceptions when working with custom serialization implementations.
