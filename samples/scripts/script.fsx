#r "../../src/Migrondi.Core/bin/Debug/net8.0/Migrondi.Core.dll"
#r "nuget: Microsoft.Extensions.Logging.Console, 9.0.0"

open Migrondi.Core
open Microsoft.Extensions.Logging

let config = MigrondiConfig.Default

let migrondi = Migrondi.MigrondiFactory(config, ".")
// Before we can talk to the database we need to initialize the migrondi object
// this will ensure that the migrations directory exists, and the required pieces
// of information are available in the database to see if it is ready to accept migrations.
migrondi.Initialize()

// Let's create a new Migration, since this is just an I/O operation
// there's no need to initialize the database yet, but ideally
// you would want to do that anyways for safety
migrondi.RunNew(
  "add-test-table",
  "create table if not exists test (id int not null primary key);",
  "drop table if exists test;"
)

// once that the migrondi service is initialized we can try to commmunicate to the
// database and in this case go for a dry run
let applied = migrondi.DryRunUp()

printfn $"List of the migrations that would have been ran:\n\n%A{applied}"