## Goal

Make the CLI the primary agent-facing surface, with MCP served per project from the CLI, a shared MCP project that the GUI can reuse later, and a GUI whose logic no longer depends on Avalonia.

## Success Criteria

- A new shared project holds the MCP tools, result types, and transports. MigrondiUI and the CLI both reference it. No MCP code stays under `src/MigrondiUI/Mcp`.
- `migrondi --json <command>` and `migrondi <command> [mi-json]` give the same JSON output for every command. Logs go to stderr in JSON mode.
- `migrondi mcp` serves one project in stdio mode, binds the project from `--config`, and writes nothing to stdout except MCP messages.
- The GUI builds and runs with its logic in an Avalonia-free layer. Only views, components, and the app entry point name Avalonia types.
- The GUI gets MCP back through the shared project, with no second tool implementation.
- Build stays at 0 warnings and 0 errors, all test suites pass, and each phase ends with a runtime run of the changed surface, not a build alone.

## Key Decisions

- **Shared project name and shape:** create `src/Migrondi.Mcp`. It depends on Migrondi.Core only. It takes an `IMigrondi` factory and a project source as inputs. It owns no config file, no database of projects, and no UI types.
- **Tools split from binding:** the tool layer works on one or more `IMigrondi` instances and knows nothing about folders. A binding layer answers how the tool layer gets its projects. The CLI binding is one project from a config path. The GUI binding is the project collection. This split is what phases 1, 3, and 5 share.
- **Project binding order for CLI MCP:** `--config <path>` first, then `MIGRONDI_*` env vars, then `cwd/migrondi.json` as the last fallback. Cwd alone is not the contract, because harnesses launch stdio servers from their own working directory.
- **Multi-project stays a GUI concern:** the CLI server serves one project per process. The shared tool layer must not hard-code that limit, so the GUI binding can serve many projects later without a tool rewrite.
- **Compatibility:** the `[mi-json]` and `[mi-debug]` tokens keep working for at least one release after the global flags land. Tool names in the CLI server match the current MCP tool names where the shape fits.

## Phase 1 — Extract the MCP tooling to a shared project

Current state, verified in the code: MCP lives in `src/MigrondiUI/Mcp` (`Server.fs`, `Tools.fs`, `Results.fs`, `Types.fs`, `HttpServer.fs`). The tools sit on `IProjectCollection` and `IMigrationOperationsFactory` in `src/MigrondiUI/Services.fs`, backed by `Database.fs` and `VirtualFs.fs`. MigrondiUI's entry point already runs MCP headless with `--stdio` or `--http` and opens no window in that mode.

Steps:

1. Move the project types out of the GUI project first. `LocalProject`, `VirtualProject`, `VirtualMigration`, and `Project` live in `src/MigrondiUI/Types.fs` (74 lines, no Avalonia in it), and the result encoders in `Results.fs` build on them. A shared project cannot reference the GUI project, so the types move into the shared project (or into Migrondi.Core) before anything else moves.
2. Create `src/Migrondi.Mcp` with the tool definitions, result encoders, and both transports moved as they are. Keep tool names, result shapes, and the `--readonly` flag behaviour identical.
3. Define the two inputs the shared project needs: a project source that lists and gets projects, and an operations factory that makes an `IMigrondi` per project. Keep the interfaces in the shared project. Make `Services.fs` implement them in place. Do not move `Database.fs` or `VirtualFs.fs`: the GUI views use the same store, so the store stays in the GUI project and implements the shared interfaces from there.
4. Keep the GUI's own database setup in the GUI host. `Server.createEnvironment` now runs the GUI database migration through `Migrations.GetMigrondi`; that call stays with the host that owns the database and never enters the shared project.
5. Point MigrondiUI at the shared project. Delete `src/MigrondiUI/Mcp` only after the GUI MCP mode passes the runtime check below.
6. Move the MCP tests from `src/MigrondiUI.Tests/McpServer.fs` to a test location that references the shared project. Keep the test cases and their assertions.

Done when: `MigrondiUI --stdio` answers `initialize`, lists the same 16 tools, and answers `list_projects` against a real client script. The GUI window still opens and runs. Exit check for phase 5: the 37 current MCP tests pass unchanged against the extracted project, with local-project result shapes byte-equal to the shapes before the move.

## Phase 2 — Define Serilog JSON output and add a global JSON flag

Current state, verified in the code: `Program.fs` scans `argv` by hand for `[mi-json]` and `[mi-debug]`. In JSON mode, Serilog writes compact JSON log lines to the console, and `list` is the only command that uses the JSON flag. Handlers return only exit code 0 or 1, and `status` returns 0 when no migration is found. The hand scan exists because the root command today has no global option that every subcommand inherits, and the argument parser is built once in `Program.fs` before any command runs.

Steps:

1. Parse before anything is built. `Program.fs` now builds the logger and the app environment before it builds and parses the root command, and the hand scan of `argv` is the workaround for that order. Change the order first: parse the root command (or pre-parse only the global flags with the same parser), then build the logger and the environment from the parse result.
2. Add `--json` as a global option on the root command so every subcommand accepts it in any position, for example `migrondi list --json` and `migrondi --json list`. Do the same for `--debug`.
3. Keep the token scan. Map `[mi-json]` to the same setting as `--json`, and `[mi-debug]` to the same setting as `--debug`. Log one warning line to stderr when a token form is used. State the shape change in the changelog: `[mi-json]` today prints JSON log lines, and under the new contract it prints one JSON value. The token keeps working; its output shape changes by design.
4. Define the JSON contract in one place: stdout carries one JSON value per command run, logs go to stderr, and each result uses one envelope with a schema version, an ok flag, the data, and a structured error with a code and a message.
5. Move every command to the contract: `init`, `new`, `up`, `down`, `list`, and `status`. Give dry runs for `up` and `down` the same shape as the real runs.
6. Give each error class its own exit code: bad arguments, no config found, bad config, migration not found, migration failed, and success with no work. Keep 0 for success.
7. Test both forms. Each command test runs once with `--json` and once with `[mi-json]` and asserts the same stdout value.

Done when: `migrondi --json list` and `migrondi list [mi-json]` print the same JSON value on stdout, stderr holds only log lines, and a shell script can run `init`, `new`, `up`, `list`, `status`, `down` in a temp folder and parse every output.

## Phase 3 — Add single-project MCP support to the CLI

Current state, verified in the code: `Env.BuildDefault` builds one config from `cwd/migrondi.json`, env vars, and CLI overrides, and one `IMigrondi` from `MigrondiFactory`. That is the whole binding the CLI server needs.

Steps:

1. Prove clean stdout with a spike first. The current GUI server writes log lines to stdout before the first MCP message, and a strict client fails on the first line. Build the smallest `mcp` stub that answers `initialize` with all logs on stderr, and pass a strict client against it before any tool work starts.
2. Add an `mcp` command to the CLI with `--stdio` as the first transport. Accept `--config <path>`, `--readonly`, and the existing config overrides (`--connection`, `--migrations`, `--table-name`, `--driver`).
3. Build the server from the shared project with a single-project binding: resolve the config by the binding order in Key Decisions, build one `IMigrondi`, and serve the read tools first — `list_migrations`, `dry_run_migrations`, `dry_run_rollback`, plus one tool that reports the resolved config path, driver, and migrations folder, and never the connection string.
4. Add the write tools after the read tools pass: `create_migration`, `run_migrations`, `run_rollback`. Match the current tool names and mark read-only and destructive tools with the same annotations the current server uses.
5. Send all logs to stderr in `mcp` mode from the first line. No preview notice, no transport log, and no Serilog console line may reach stdout.
6. Add a harness config example to the docs: a client entry with `command`, `args` set to `mcp --config <path>`, and no need for a working directory.

Done when: a strict MCP client that rejects any non-JSON line on stdout completes `initialize`, `tools/list`, `list_projects` or its single-project equal, a dry run, and one real apply and rollback against a temp SQLite project.

## Phase 4 — Segment the GUI away from Avalonia

Current state, verified in the code: `Services.fs`, `Projects.fs`, `Database.fs`, and `VirtualFs.fs` name no Avalonia type. Avalonia enters through `Views`, `Components`, `Program.fs`, and `Extensions.fs`. The view models hold `FSharp.Data.Adaptive` values and receive an `INavigable<Control>`, so control types leak into logic through navigation and bindings.

Steps:

1. Split each view file into a logic file and a view file first. The view models live inside the view files today (`LandingVM` sits in `Landing.fs`), and four views take an `INavigable<Control>` while two view model methods take a `Control`. The split is the work; the navigation contract in step 4 cannot change until it is done.
2. List every Avalonia type that logic files name today. The list is the work queue. Do not redesign screens in this phase.
3. Move view models and page logic into files that take plain values and return plain values. Keep commands and state in the logic layer. Keep control building in views and components only.
4. Replace the navigation input that logic receives. Logic asks for a route change by name or URL and gets a result value. The Avalonia layer owns the router and turns that request into a control. Logic never sees `INavigable<Control>`.
5. Replace binding helpers that logic uses with values the view can bind. Logic exposes state as adaptive values or plain events. The view owns `AVal.toBinding` and all control updates.
6. Move `Extensions.fs` helpers that logic needs into the logic layer under their own names. Leave only control helpers in the Avalonia layer.
7. Add a build check that the logic layer names no Avalonia, SukiUI, or NXUI type. A grep over the logic files in CI is enough.

Done when: the grep check passes, the GUI opens on a real display, and a user can list projects, open one local and one virtual project, run a dry run, and apply and roll back one migration. Headless tests alone do not close this phase, because Avalonia failures show at runtime.

## Phase 5 — Bring MCP back to the GUI in the new shape

Current state: after phase 1 the GUI MCP mode runs on the shared project with the project collection binding. This phase is the clean-up and the proof, not a second server.

Steps:

1. Delete any MCP code left in MigrondiUI. The GUI keeps only the binding: its project collection and operations factory as inputs to the shared project.
2. Keep both GUI transports, `--stdio` and `--http`, on the shared server. Keep `--readonly` working for both hosts.
3. Make the GUI server report its binding the same way the CLI server does, per project, with no connection strings in tool results.
4. Add one contract test set that runs the same tool calls against the CLI single-project server and the GUI multi-project server and compares result shapes. Run it against both hosts in CI.
5. Update the docs: one page for CLI MCP, one page for GUI MCP, and a short note that states which host to use for one project and which for many.

Done when: one agent client can run the same migration flow against both hosts, and the GUI window and the GUI MCP mode both start from the same binary with no shared code path broken.

## Validation Plan

- After each phase: `dotnet build Migrondi.slnx --configuration Release` gives 0 warnings and 0 errors, and `dotnet test Migrondi.slnx --configuration Release` gives all tests green.
- Phase 1 and 5 runtime check: script an MCP client that fails on any non-JSON stdout line. Run `initialize`, `tools/list`, and one read tool and one write tool against each host.
- Phase 2 runtime check: run the six CLI commands in a temp folder with a SQLite database, in both flag forms, and parse every stdout value with a JSON parser.
- Phase 3 runtime check: start `migrondi mcp` from a folder that is not the project folder, with only `--config` in the client entry. Complete one apply and one rollback.
- Phase 4 runtime check: open the GUI on a real display and run the project flow by hand. A build and the headless suite do not count as proof for this phase.
- Phase 5 runtime check: run the same client script against the CLI host and the GUI host and compare the tool results.

## Risks and Non-Goals

- Navs.Avalonia stays at 1.0.0 for this roadmap. Version 2 changes the routing API and removes helpers the GUI uses. That move is GUI work and sits outside these phases.
- The shared project must not grow a project registry to make phase 3 easy. A registry in the shared layer pulls the GUI database into the CLI path and breaks the single-project binding.
- Tool result shapes may change only in phase 1 if a shape names a GUI type. Any other shape change needs a note in the changelog under `Unreleased`.
- No new GUI features in these phases. GUI work here is extraction and segmentation only.
- No multi-project CLI server in these phases. One agent client starts one CLI server per project.
