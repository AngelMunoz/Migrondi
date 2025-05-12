(**
---
title: FileSystem Service
category: Core
categoryindex: 3
index: 2
---

The `IMiFileSystem` object is one of the most likely object to replace if you're implementing your own storage mechanism. It is used to read and write files.

*)

#r "nuget: Migrondi.Core, 1.0.0-beta-012"

open System
open Migrondi.Core.FileSystem
open Migrondi.Core.Serialization
open Microsoft.Extensions.Logging

let logger =
  // create a sample logger, you can provide your own
  LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)
  |> fun x -> x.CreateLogger("FileSystem")

let serializer = MigrondiSerializer()

let rootDir = Uri("https://my-storage.com/project-a/", UriKind.Absolute)
let migrationsDir = Uri("./migrations/", UriKind.Relative)

(**
> ***NOTE***: Please keep in mind that URI objects treat trailing shashes differently, since we're using them to represent directories. The `Uri` object must contain the trailing slash from the `rootDir` and `migrationsDir` objects.
> If you fail to ensure the trailing slash is there, the library will not work as expected.
*)

let fs: IMiFileSystem =
  MiFileSystem(logger, serializer, serializer, rootDir, migrationsDir)

(**
In order to list the migrations in the file system, you can call the `ListMigrations(targetDirectory)` method.
*)

fs.ListMigrations("./")

(**
Internally this will build the URI from the `rootDir` and `migrationsDir` objects, and then list the files in the directory.

So you have to keep into consideration how will you take external paths and convert them into URIs to be used by the library.
This is mainly an implementation detail, but it's important to keep in mind when you're implementing your own `IMiFileSystem` object.
A simplified way to list the migrations internally that we currently do is similar to the following:
*)

// usually "readFrom" is the content of config.migrationsDir
let listMigrations (readFrom: string) =
  let path = Uri(rootDir, readFrom)
  let dir = IO.DirectoryInfo(path.LocalPath)
  // list all files in the directory
  // filter out the ones that are migrations (we have a specific schema for the file name)
  // for each found file, decode the text and return  migration object otherwise return an error
  []

(**
When you read a migration file specifically you can call the `ReadMigration(readFrom)` method.

This readFrom is the relative path to the `migrationsDir` objects including the name of the file.
e.g.
*)

fs.ReadMigration("initial-tables_1708216610033.sql")

(**
This will build the URI from the `rootDir` and `migrationsDir` objects, and then read the file. Internally it looks like this:
*)

let readMigration (readFrom: string) =
  let path = Uri(rootDir, Uri(migrationsDir, readFrom))
  let file = IO.File.ReadAllText(path.LocalPath)
  let migration = (serializer :> IMiMigrationSerializer).DecodeText(file)
  migration

(**
Writing a migration is similar, and usually we don't write migrations directly, but we do it through the serializers.
*)


fs.WriteMigration(
  {
    name = "initial-tables_1708216610033"
    timestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds()
    upContent = "CREATE TABLE users (id INT, name VARCHAR(255));"
    downContent = "DROP TABLE users;"
    manualTransaction = false
  },
  "initial-tables_1708216610033.sql"
)

(**
In this case it just means that you need to know how you will want to store the migrations in your own backing store.
*)