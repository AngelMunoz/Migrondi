(**
---
title: Custom Migration Source
category: Core
categoryindex: 3
index: 2
---

The `IMiMigrationSource` interface is the primary extension point for implementing custom storage mechanisms. It allows you to provide raw migration and configuration content from any source (HTTP, S3, In-Memory, etc.), while the library handles all internal logic for parsing and managing migrations.
*)

#r "../../src/Migrondi.Core/bin/Debug/net8.0/Migrondi.Core.dll"
#r "nuget: Microsoft.Extensions.Logging.Console, 9.0.0"

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open System.Runtime.InteropServices
open Migrondi.Core
open Migrondi.Core.FileSystem

let logger =
  // create a sample logger, you can provide your own
  LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
  |> fun x -> x.CreateLogger("FileSystem")

// Example URIs for documentation purposes
let rootDir = Uri("https://example.com/project/", UriKind.Absolute)
let migrationsDir = Uri("migrations/", UriKind.Relative)

(**
> ***NOTE***: Please keep in mind that URI objects treat trailing slashes differently, since we're using them to represent directories. The `Uri` object must contain the trailing slash from the `rootDir` and `migrationsDir` objects.
> If you fail to ensure the trailing slash is there, the library will not work as expected.
*)

(**
## URI-based Path Handling

Migrondi uses URIs internally to represent both local and remote paths. This abstraction allows you to implement sources that work with:
- Local file system (using file:// URIs)
- Cloud storage (using http:// or https:// URIs)
- Virtual file systems
- Databases as file storage

The migration source is used by the internal file system service. You can provide your own implementation via the `source` parameter in `MigrondiFactory`.
*)

(**
## Custom Migration Sources

To implement a custom storage backend, you implement the `IMiMigrationSource` interface. This interface deals strictly with raw strings and URIs, ensuring you don't have to worry about the internal migration format or serialization.

Using an object expression is the recommended way to implement this interface:
*)

let createCustomSource (logger: ILogger) =
  { new IMiMigrationSource with
      member _.ReadContent(uri: Uri) =
        logger.LogDebug("Reading content from {Path}", uri.ToString())
        // Implement your sync read logic here
        "SQL CONTENT"

      member _.ReadContentAsync
        (uri: Uri, [<Optional>] ?cancellationToken: CancellationToken)
        =
        task {
          logger.LogDebug(
            "Reading content asynchronously from {Path}",
            uri.ToString()
          )
          // Implement your async read logic here
          return "SQL CONTENT"
        }

      member _.WriteContent(uri: Uri, content: string) =
        logger.LogDebug("Writing content to {Path}", uri.ToString())
        // Implement your sync write logic here
        ()

      member _.WriteContentAsync
        (
          uri: Uri,
          content: string,
          [<Optional>] ?cancellationToken: CancellationToken
        ) =
        task {
          logger.LogDebug(
            "Writing content asynchronously to {Path}",
            uri.ToString()
          )
          // Implement your async write logic here
          ()
        }

      member _.ListFiles(locationUri: Uri) =
        logger.LogDebug("Listing files in {Path}", locationUri.ToString())
        // Return a sequence of URIs for migrations at this location
        Seq.empty

      member _.ListFilesAsync
        (locationUri: Uri, [<Optional>] ?cancellationToken: CancellationToken)
        =
        task {
          logger.LogDebug(
            "Listing files asynchronously in {Path}",
            locationUri.ToString()
          )
          // Return URIs asynchronously
          return Seq.empty
        }
  }

(**
## Usage with MigrondiFactory

Once you have your custom source, you can pass it to the factory. Migrondi will wrap it in its internal file system service, providing all the listing, filtering, and parsing logic automatically.

```fsharp
let config = MigrondiConfig.Default
let mySource = createCustomSource logger

let migrondi = Migrondi.MigrondiFactory(
    config,
    rootDirectory = ".",
    source = mySource
)
```

## Benefits of the Source Abstraction

By using `IMiMigrationSource` instead of implementing the full file system:
1. **Simplified API**: You only handle raw string transfers.
2. **Encapsulated Serialization**: You don't need to know how Migrondi encodes migrations (Regex, JSON, etc.).
3. **Internal Consistency**: The library ensures that migrations are listed, filtered, and ordered correctly regardless of where they are stored.
*)