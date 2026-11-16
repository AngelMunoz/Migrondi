# Migrondi as an MCP Server

Migrondi serves its migrations over MCP (Model Context Protocol), so MCP clients and agents can work with migration projects as tools. The main host is the CLI: `migrondi mcp` serves one project over stdio, and because the server is stateless, a single registration can also drive any project on the machine by passing `configPath` per tool call. The desktop app is the other host: `migrondiui --http` serves the projects the app manages over HTTP. Use the CLI unless the projects you need are the ones registered in the desktop app; both hosts return the same result shapes for the tools they share.

## The CLI host

```
migrondi mcp                                  # serve the project in the working directory
migrondi mcp --config path/to/migrondi.json   # serve the project that config anchors
migrondi mcp --readonly                       # serve only the read tools
```

Stdio is the CLI's transport: `--stdio` is accepted and is the default, and `--http` is rejected with an error. All server logs go to stderr, so stdout carries only MCP protocol messages and the server is safe to plug into strict clients.

The CLI serves nine tools: `get_project`, `list_migrations`, `dry_run_migrations`, `dry_run_rollback`, `create_migration`, `run_migrations`, `run_rollback`, `init_project`, and `update_project_config`. Every tool takes `configPath` as its first argument — a path to target that project, or `null` to use the startup binding — and the server re-reads the project config on every call. See the [CLI MCP section](cli.md) for configuration resolution, the explicit-`null` convention for optional arguments, and a harness configuration example.

Client entry — `--config` is optional, so register the server once with no project pinned:

```json
{
  "mcpServers": {
    "migrondi": {
      "command": "migrondi",
      "args": ["mcp"]
    }
  }
}
```

Each tool call then names its project in the `configPath` argument (or sends `null` to use the project in the server's working directory):

```json
{ "configPath": "/absolute/path/to/migrondi.json", "amount": null }
```

Pinning `--config <path>` at startup is also supported when a client only ever works with one project; per-call `configPath` still overrides it.

## The desktop app host

The desktop app serves the project collection it manages — the local and virtual projects stored in the app's own database — over Streamable HTTP:

```
migrondiui --http 8080    # serve over Streamable HTTP on localhost:8080/mcp/
Migrondi.UI --http 8080   # the same, from the binary archive
```

Add `--readonly` to serve only the 6 read tools (`list_projects`, `get_project`, `list_migrations`, `get_migration`, `dry_run_migrations`, `dry_run_rollback`). Without `--readonly` the server exposes all 16 tools. All server logs go to stderr; in this mode the app does not open a window. The app serves HTTP only: passing `--stdio` prints a line pointing at `migrondi mcp` and exits.

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

Client entry:

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

Start with the CLI host: it serves a project straight from its `migrondi.json`, needs no app state, and one registration can reach any project through `configPath`. Use the desktop app host when the projects to work with are the ones the app already manages, especially virtual projects and their lifecycle tools. Shared tools return the same result shapes on both hosts.
