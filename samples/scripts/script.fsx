#r "nuget: Migrondi.Core, 1.0.0-beta-009"

open System
open System.IO
open Migrondi.Core

let config = MigrondiConfig.Default

let migrondi = Migrondi.MigrondiFactory(config, ".")

// Let's create a new Migration, since this is just an I/O operation
// there's no need to initialize the database yet, but ideally
// you would want to do that anyways for safety
migrondi.RunNew(
  "add-test-table",
  "create table if not exists test (id int not null primary key);",
  "drop table if exists test;"
)

// Before we can talk to the database we need to initialize the migrondi object
// this will ensure that the migrations directory exists, and the required pieces
// of information are available in the database to see if it is ready to accept migrations.
migrondi.Initialize()

// once that the migrondi service is initialized we can try to commmunicate to the
// database and in this case go for a dry run
let applied = migrondi.DryRunUp()

printfn
  $"List of the migrations that would have been ran:\n\n%A{applied}"
