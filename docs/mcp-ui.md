# Migrondi.UI as an MCP Server

The Migrondi.UI desktop app doubles as an MCP (Model Context Protocol) server over HTTP: run the app binary with `--http` and MCP clients can drive the migration projects the app manages as tools, instead of clicking through the window. The `migrondi` CLI owns stdio: pass `--stdio` to the GUI binary and it prints one line pointing at `migrondi mcp --stdio` and exits.

## Running the server

```
Migrondi.UI --http 8080    # serve over Streamable HTTP on localhost:8080/mcp/
```

Add `--readonly` to serve only the 6 read tools (`list_projects`, `get_project`, `list_migrations`, `get_migration`, `dry_run_migrations`, `dry_run_rollback`). Without `--readonly` the server exposes all 16 tools.

All server logs go to stderr, so the HTTP stream carries only MCP protocol messages. In this mode the app does not open a window.

## Tools

The host serves the projects the desktop app manages (the project collection stored in the app's own database).

Read tools:

- `list_projects` — lists the local and virtual projects
- `get_project` — reports a project's detail (name, driver, migrations table, kind; never the connection string)
- `list_migrations` — lists migrations with their applied/pending status
- `get_migration` — reports one migration's full content
- `dry_run_migrations` — previews the pending migrations that would be applied
- `dry_run_rollback` — previews the applied migrations that would be reverted

Write tools:

- `create_migration` — creates a new migration, optionally with `upContent`/`downContent` SQL
- `run_migrations` — applies pending migrations (optionally a count)
- `run_rollback` — reverts applied migrations (optionally a count)
- `update_migration` — rewrites a migration's up/down content
- `delete_migration` — deletes a migration
- `create_virtual_project` — registers a new virtual project in the app database
- `update_virtual_project` — edits a virtual project's name, connection, table, or driver
- `delete_project` — removes a virtual project
- `export_virtual_project` — writes a virtual project to disk as a standard Migrondi project
- `import_from_local` — registers an on-disk `migrondi.json` project as a virtual project

MCP operations target **virtual projects**: the project lifecycle tools (`create_virtual_project`, `update_virtual_project`, `delete_project`, `export_virtual_project`, `import_from_local`) and `get_migration` work on virtual projects. Local projects appear in `list_projects` and `get_project`, and the migration operations (create, run, rollback, dry runs, list) work against any project the app manages.

## Client entry

Point the client at the Migrondi.UI binary's HTTP endpoint:

```json
{
  "mcpServers": {
    "migrondiui": {
      "url": "http://localhost:8080/mcp/"
    }
  }
}
```

## Which host

Use the CLI host (`migrondi mcp`) when an agent drives one project pinned by `--config`; the CLI serves stdio. Use the GUI host (`Migrondi.UI --http`) for the many projects managed by the desktop app. Both hosts serve the same result shapes for the shared migration tools; see the [CLI MCP section](cli.md) for the single-project tool set.
