(**
---
title: FileSystem Service
category: Core
categoryindex: 3
index: 2
---

The `IMiFileSystem` object is one of most likely interfaces to replace if you're implementing your own storage mechanism. It is used to read and write files.
*)

#r "../../src/Migrondi.Core/bin/Debug/net8.0/Migrondi.Core.dll"
#r "nuget: Microsoft.Extensions.Logging.Console, 9.0.0"
#r "nuget: FsToolkit.ErrorHandling"

open System
open Migrondi.Core
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization
open Microsoft.Extensions.Logging

let logger =
  // create a sample logger, you can provide your own
  LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
  |> fun x -> x.CreateLogger("FileSystem")

let serializer = MigrondiSerializer()

// Example URIs for documentation purposes
let rootDir = Uri("https://example.com/project/", UriKind.Absolute)
let migrationsDir = Uri("migrations/", UriKind.Relative)

(**
> ***NOTE***: Please keep in mind that URI objects treat trailing slashes differently, since we're using them to represent directories. The `Uri` object must contain the trailing slash from the `rootDir` and `migrationsDir` objects.
> If you fail to ensure the trailing slash is there, the library will not work as expected.
*)

(**
## URI-based Path Handling

Migrondi uses URIs internally to represent both local and remote paths. This abstraction allows you to implement file systems that work with:
- Local file system (using file:// URIs)
- Cloud storage (using http:// or https:// URIs)
- Virtual file systems
- Databases as file storage

The file system service is provided internally when you use `MigrondiFactory`. You can provide your own implementation via the experimental factory overload.
*)

(**
## Reading Migrations

The file system service can list and read migration files. When you use the main Migrondi service, this happens automatically.

To list migrations, the service:
1. Builds a URI from the `rootDir` and `migrationsDir` objects
2. Lists files in the directory
3. Filters out ones that match the migration filename schema
4. Reads and decodes each migration file

Example of what this looks like internally:
*)

let listMigrations (readFrom: string) =
  let path = Uri(rootDir, readFrom)
  let dir = IO.DirectoryInfo(path.LocalPath)
  // list all files in the directory
  // filter out the ones that are migrations (we have a specific schema for the file name)
  // for each found file, decode the text and return migration object
  []

(**
When you read a specific migration file, the path is relative to the migrations directory:
*)

// Example: "1708216610033_initial-tables.sql"
// This would be read from: rootDir + migrationsDir + "1708216610033_initial-tables.sql"

let readMigration (readFrom: string) =
  let path = Uri(rootDir, Uri(migrationsDir, readFrom))
  let file = IO.File.ReadAllText(path.LocalPath)
  let migration = (serializer :> IMiMigrationSerializer).DecodeText(file)
  migration

(**
## Writing Migrations

Writing a migration serializes it to either text or JSON format and saves it to the configured location.

Typically, the main Migrondi service handles writing migrations when you call `RunNew()`. Here's what that looks like:
*)

let writeMigration (migration: Migration) (filename: string) =
  let content = (serializer :> IMiMigrationSerializer).EncodeText(migration)
  let path = Uri(rootDir, Uri(migrationsDir, filename))
  IO.File.WriteAllText(path.LocalPath, content)

(**
## Configuration Files

The file system also handles reading and writing the `migrondi.json` configuration file.
*)

// Reading configuration
let readConfig (configPath: string) =
  let fullPath = Uri(rootDir, configPath)
  let content = IO.File.ReadAllText(fullPath.LocalPath)
  let config = (serializer :> IMiConfigurationSerializer).Decode(content)
  config

// Writing configuration
let writeConfig (config: MigrondiConfig) (configPath: string) =
  let content = (serializer :> IMiConfigurationSerializer).Encode(config)
  let fullPath = Uri(rootDir, configPath)
  IO.File.WriteAllText(fullPath.LocalPath, content)

(**
## Custom File System Implementations

You can implement your own `IMiFileSystem` interface to work with different storage backends:

```fsharp
type MyCloudFileSystem(logger, serializer, rootUri, migrationsUri) =
  interface IMiFileSystem with
    member this.ReadMigration(name) =
      // Read from cloud storage
      let content = downloadFromCloud(name)
      (serializer :> IMiMigrationSerializer).DecodeText(content)

    member this.WriteMigration(migration, name) =
      // Upload to cloud storage
      let content = (serializer :> IMiMigrationSerializer).EncodeText(migration)
      uploadToCloud(name, content)
```

Then use the experimental factory overload:

```fsharp
let customFS = MyCloudFileSystem(logger, serializer, rootUri, migrationsUri)
let migrondi = Migrondi.MigrondiFactory(config, ".", miFileSystem = customFS)
```
*)