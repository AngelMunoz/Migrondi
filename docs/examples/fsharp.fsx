(**
---
title: F# Scripts
category: Examples
categoryindex: 1
---
Migrondi can be used in F# scripts, which is a great way to get the hang of the library and easy to prototype.

This example adds the most simple way to get started, the default migrondi behavior is used and the API reflects the CLI commands.

To begin we need to add the Migrondi.Core package to the script, this can be done by adding the following line to the top of the script:

*)
#r "nuget: Migrondi.Core, 1.0.0-beta-012"

open Migrondi.Core

(**
The next step is to create a new Migrondi object, this object will be used to interact with the database and the migrations.

Migrondi works with a "root" directory, so the paths to the database and directories are relative to this root directory.
The default migrondi configuration uses an sqlite database relative to the in the root directory.
*)
let config = MigrondiConfig.Default

// In this context "." means the current directory
// But you can specify a Absolute Path here e.g. C:\Users\user\project\ or /home/user/project/
// all of the relative files used in the configuration will be relative to this specified path
let migrondi = Migrondi.MigrondiFactory(config, ".", ?logger = None)

(**
There are certain operations like creating a new migration, or initializing the directory with the migrondi files.
These operations can run before initializing the database.
*)
migrondi.RunNew(
  "add-test-table",
  "create table if not exists test (id int not null primary key);",
  "drop table if exists test;"
)

(**
For operations that require database access like listing, up, down, and similar
We need to initialize the database, which is jargon to say we need to create the required tables, initialize the driver and so on.
we use [RepoDB](https://repodb.net/) under the hood, so we need to initialize the database before we can run any operations.
*)
migrondi.Initialize()

// once that the migrondi service is initialized we can try to commmunicate to the
// database and in this case go for a dry run
let applied = migrondi.DryRunUp()

printfn $"List of the migrations that would have been ran:\n\n%A{applied}"