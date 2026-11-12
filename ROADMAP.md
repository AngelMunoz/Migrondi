## Goal

Make the CLI the primary agent-facing surface, with MCP served per project from the CLI, a shared MCP project that the GUI can reuse later, and a GUI whose logic no longer depends on Avalonia.

## Success Criteria

- A new shared project holds the MCP tools, result types, and transports. MigrondiUI and the CLI both reference it. No MCP code stays under `src/MigrondiUI/Mcp`.
- An agent can drive the CLI with single-purpose commands: create a migration with its SQL in one `new` call, discover migrations with a filtered `list`, and apply or roll back to a named migration with `up --to` / `down --to`, orchestrating the sequence itself. CLI output formats stay as they are; structured results are the MCP layer's job.
- `migrondi mcp` serves one project in stdio mode, binds the project from `--config`, and writes nothing to stdout except MCP messages.
- The GUI builds and runs with its logic in an Avalonia-free layer. Only views, components, and the app entry point name Avalonia types.
- The GUI gets MCP back through the shared project, with no second tool implementation.
- Build stays at 0 warnings and 0 errors, all test suites pass, and each phase ends with a runtime run of the changed surface, not a build alone.

## Key Decisions

- **Shared project name and shape:** create `src/Migrondi.Mcp`. It depends on Migrondi.Core only. It takes an `IMigrondi` factory and a project source as inputs. It owns config resolution and project file writing; still no registry, no persistence, and no UI types.
- **Tools split from binding:** the tool layer works on one or more `IMigrondi` instances and knows nothing about folders. A binding layer answers how the tool layer gets its projects. The CLI binding is one project from a config path. The GUI binding is the project collection. This split is what phases 1, 3, and 5 share.
- **Project binding order for CLI MCP:** `--config <path>` first, then `MIGRONDI_*` env vars, then `cwd/migrondi.json` as the last fallback. Cwd alone is not the contract, because harnesses launch stdio servers from their own working directory. When `--config` is given, its containing directory becomes the project root, so the migrations folder and a relative SQLite path resolve against that directory, not the server's working directory. That anchoring is an MCP-binding decision in phase 3; the plain CLI keeps its current model, where the project root is the invocation directory and the config is always `<cwd>/migrondi.json`.
- **Multi-project stays a GUI concern:** the CLI server is stateless — it resolves a default binding at startup only to fail fast on a bad `--config`, and every tool call resolves its own binding (`configPath` argument, else the startup default, else the cwd chain), builds its own `IMigrondi`, and runs. One registration can drive any project on the machine; a per-workspace registration pins `--config` for the zero-argument UX. The shared tool layer must not hard-code a project limit, so the GUI binding can serve many projects without a tool rewrite.
- **Spec target:** the MCP surfaces target spec revision 2026-07-28 through the official C# SDK (`ModelContextProtocol` 2.x; 2.4.0 as of this writing). Era negotiation (`server/discover` with the legacy `initialize` fallback) and per-request `_meta` belong to the SDK; the shared layer never hand-rolls handshake or versioning logic. Roots, Sampling, and the Logging capability are deprecated by that revision (SDK warning MCP9005) and stay out of the shared layer.
- **CLI commands stay single-purpose:** each command does one thing and agents orchestrate the sequence of calls. Fused workflows (create-and-apply, create-and-preview, and similar) belong to the MCP tools, not to new CLI flags such as `new --apply`.
- **Compatibility:** the `[mi-json]` and `[mi-debug]` tokens keep working with their current output shapes; phase 2 does not change CLI output formats. Tool names in the CLI server match the current MCP tool names where the shape fits.

## Phase 1 — Extract the MCP tooling to a shared project

Status: done. The shared project is `src/Migrondi.Mcp`; MigrondiUI keeps only the host file `src/MigrondiUI/Mcp/Server.fs`. The 37 MCP tests pass unchanged, and the GUI MCP mode answers `initialize`, lists the same 16 tools, and answers `list_projects` from a real client.

Current state, verified in the code: MCP lives in `src/MigrondiUI/Mcp` (`Server.fs`, `Tools.fs`, `Results.fs`, `Types.fs`, `HttpServer.fs`). The tools sit on `IProjectCollection` and `IMigrationOperationsFactory` in `src/MigrondiUI/Services.fs`, backed by `Database.fs` and `VirtualFs.fs`. MigrondiUI's entry point already runs MCP headless with `--stdio` or `--http` and opens no window in that mode.

Steps:

1. Move the project types out of the GUI project first. `LocalProject`, `VirtualProject`, `VirtualMigration`, and `Project` live in `src/MigrondiUI/Types.fs` (74 lines, no Avalonia in it), and the result encoders in `Results.fs` build on them. A shared project cannot reference the GUI project, so the types move into the shared project (or into Migrondi.Core) before anything else moves.
2. Create `src/Migrondi.Mcp` with the tool definitions, result encoders, and both transports moved as they are. Keep tool names, result shapes, and the `--readonly` flag behaviour identical.
3. Define the two inputs the shared project needs: a project source that lists and gets projects, and an operations factory that makes an `IMigrondi` per project. Keep the interfaces in the shared project. Make `Services.fs` implement them in place. Do not move `Database.fs` or `VirtualFs.fs`: the GUI views use the same store, so the store stays in the GUI project and implements the shared interfaces from there.
4. Keep the GUI's own database setup in the GUI host. `Server.createEnvironment` now runs the GUI database migration through `Migrations.GetMigrondi`; that call stays with the host that owns the database and never enters the shared project.
5. Point MigrondiUI at the shared project. Delete `src/MigrondiUI/Mcp` only after the GUI MCP mode passes the runtime check below.
6. Move the MCP tests from `src/MigrondiUI.Tests/McpServer.fs` to a test location that references the shared project. Keep the test cases and their assertions.

Done when: `MigrondiUI --stdio` answers `initialize`, lists the same 16 tools, and answers `list_projects` against a real client script. The GUI window still opens and runs. Exit check for phase 5: the 37 current MCP tests pass unchanged against the extracted project, with local-project result shapes byte-equal to the shapes before the move.

## Phase 2 — Agent-facing flags for the existing CLI commands

Status: done. `new --up/--down/--up-file/--down-file`, the `list` discovery filters, `up`/`down --to`, and the non-zero `status` exit code landed with command-level tests (#58-#61), and the `migrondi-cli` and `migrondi-library` skills live under `.agents/skills/`.

Goal: let an agent manipulate migrations through the existing commands, each of which keeps doing exactly one thing. The agent orchestrates the sequence (`new`, then `up --dry`, then `up`, then `status`); fused workflows such as `new --apply` are not CLI flags and belong to the MCP tools instead. Output formats do not change in this phase — plain logs and tables stay as they are, `[mi-json]` and `[mi-debug]` keep their current shapes, and structured results remain the MCP layer's job.

Current state, verified in the code: the CLI handler for `new` passes only the name and `--manual-transaction` to `IMigrondi.RunNew`, even though the core already accepts optional up and down content, so an agent must create an empty file and then find and edit the timestamped file itself. `list` accepts only `--kind up|down` (meaning applied and pending) with no limit, search, or path/content display. `up` and `down` take an optional positional amount and `--dry`, but no named target, so an agent must list, count, and pass a number. `status` logs `NotFound` but still returns exit code 0. The project root is the invocation directory: the config is read from `<cwd>/migrondi.json`, and the migrations folder and a relative SQLite path resolve against that directory. This phase adds no `--config` flag to the plain CLI and does not change that anchoring.

Steps:

1. Create migrations with their content. Add `--up <sql>` and `--down <sql>` to `new`, plus `--up-file <path>` and `--down-file <path>`, where `-` as the path reads that section from stdin. Pass the content through to the `upContent` and `downContent` arguments the core `RunNew` already accepts. Inline and file input for the same section are mutually exclusive and fail with an error instead of silently picking one. Omitting both keeps today's empty template sections, and `--manual-transaction` keeps working alongside the new flags. `new` still only creates the file.
2. Make `list` useful for discovery. Add `--pending` and `--applied` as the readable spellings (keeping `--kind up|down` working), `--limit <n>` and `--last <n>` to bound the result, `--search <text>` to match against migration names and SQL content, and `--show-path` and `--show-content` so an agent can find the real timestamped filename and read a migration without a separate file hunt. `list` still only lists.
3. Target `up` and `down` by name. Keep the positional amount optional exactly as today, and add `--to <name>`, mutually exclusive with the amount. The name resolves the same way `status` resolves it: migration name, or filename with or without `.sql`. Both directions end in the same state — the target is the last applied migration: `up --to <name>` applies pending migrations in timestamp order up to and including the target, and `down --to <name>` rolls back the applied migrations after the target, leaving the target applied. `--dry` selects the same set without running it. An unknown target fails; a target that is already the last applied migration is a successful no-op for both directions; a `down --to` target that is not applied fails. Derive the amount in the CLI from the ordered migration list; add no core API unless tests show the CLI cannot express the selection.
4. Make `status` trustworthy for scripts. Keep the log wording, but return a non-zero exit code when no migration matches, instead of today's 0. Applied and pending keep returning 0.
5. Hold the boundaries while doing the above. No combined or workflow flags, no output-format changes, no new sink or JSON envelope, and no change to config resolution or relative pathing. `init` keeps doing only project setup.
6. Test every flag at the command level: `new` with inline SQL, file SQL, and stdin, plus the inline/file conflict; each `list` filter on its own and combined; `up --to` and `down --to` at the boundaries (target first, target last, already at target, unknown target, `--to` with an amount, `--dry --to`); and the `status` exit codes for applied, pending, and not found. Add a changelog entry under `Unreleased`.
7. Create the agent skills now that the surface is settled. Add `.agents/skills/migrondi-cli/SKILL.md` covering the final commands and flags, the config and relative-path model (project root is the invocation directory), the V1 file rules that must never be edited, and the separated command sequence an agent should orchestrate. Add `.agents/skills/migrondi-library/SKILL.md` covering `MigrondiFactory` and the `IMigrondi` sync/async surface (including `RunNew` with content) and the custom migration source pattern: implement `IMiMigrationSource` for non-filesystem storage, not the internal filesystem interface, and respect the trailing-slash URI rule for directories. Validate both with `muse skills validate .agents/skills/<skill-id> --json` and require `valid: true` with no diagnostics. Skills are written last because they document this phase's final flags; any later flag change updates the CLI skill in the same change.

Done when: in a temp folder with a SQLite database, an agent using only separate invocations can run `init`, create two migrations with `new --up ... --down ...`, see them with `list --pending`, preview with `up --dry`, apply only the first with `up --to <name>`, apply the rest with `up`, roll back to the first with `down --to <name>`, confirm each state with `status` (and a non-zero exit for an unknown name), and finish with `down`. Both skills validate cleanly.

## Phase 2.5 — Harden the shared MCP layer for both bindings

Goal: make `src/Migrondi.Mcp` correct against the current spec revision (2026-07-28) and equally solid for the two bindings that sit on it — the CLI single-project server (phase 3) and the multi-project server (phase 5). This is shared-layer work only: `src/Migrondi.Mcp` and its tests. No GUI app code changes; the multi-project binding is exercised through the test host.

Current state, verified in the code and against SDK 2.4.0: the SDK is current and already speaks the 2026-07-28 revision, so era negotiation comes from the SDK, not from Migrondi code. Four things lag behind it:

- Clean stdout is a host's responsibility, and hosts can get it wrong: the GUI entry wires Serilog's console sink to stdout and logs preview notices before the server starts. That host defect is deferred to the GUI rework and is out of scope here; the shared layer's job is to make the correct wiring a contract hosts consume rather than re-invent.
- Tool annotations are partial: `ReadOnly` and `Destructive` are set; `Idempotent` and `OpenWorld` are never set (both are settable on `McpServerToolCreateOptions`).
- The server identity hardcodes `Version = "1.2.0"` while the real version is derived from `CHANGELOG.md` at pack time.
- The HTTP session layer predates the SDK's stateless serving: `StreamableHttpServerTransport` now has a `Stateless` init property, and the hand-rolled session store re-sends the `Mcp-Session-Id` header on every POST instead of only on the response that creates the session.

Steps:

1. Make clean stdout a shared-layer contract. The shared project provides the MCP-mode logging setup that writes every console line to stderr, and the phase 3 CLI host consumes it instead of wiring its own. The subprocess proof stays with phase 3's spike; here the contract is that hosts cannot inherit a stdout logger from the shared layer.
2. Complete the tool annotations in the shared layer: `OpenWorld = false` on every tool (they touch local files and databases only), `Idempotent = true` on the pure reads, keep `ReadOnly`/`Destructive` as set. Define the annotation matrix once in the shared layer so the phase 3 single-project tools inherit it.
3. Report the real version: derive the server `Implementation` version from assembly metadata (the pack-time version from `CHANGELOG.md`) and delete the `"1.2.0"` literal.
4. Bring HTTP serving up to the SDK's model. Set `Stateless` on the transport if stateless serving covers the multi-project HTTP use case, and delete the hand-rolled session store; otherwise keep stateful sessions and record why. Scope the `Mcp-Session-Id` response header to the response that creates the session. Reject requests whose `Origin` header is present and not a localhost origin (a spec MUST for Streamable HTTP; the listener binds localhost).
5. Guard the layer: the 37 MCP tests pass unchanged, tool call result shapes do not change (annotations are `tools/list` metadata, not call results), and the build stays at 0 warnings — an MCP9005 deprecation warning would mean Roots, Sampling, or the Logging capability entered the shared layer; they stay out.

Done when: in the test host, the shared server's `tools/list` reports `idempotentHint` on the read tools and `openWorldHint: false` everywhere; the reported server version equals the package version; the shared HTTP server, driven by a real MCP client over HTTP, completes `initialize`, `tools/list`, a dry run, an apply, and a rollback, issues `Mcp-Session-Id` only on the response that creates the session, and rejects a non-localhost `Origin`; and the 37 MCP tests pass unchanged.

## Phase 3 — Add single-project MCP support to the CLI

Status: done. `migrondi mcp` serves nine stateless tools over stdio (get_project, list/dry-run/run/rollback, create with manualTransaction, init_project, update_project_config); every tool accepts `configPath` per call, bindings live in the shared layer (`ProjectBinding`, `ProjectInit`), and a strict-client e2e suite drives the real binary through initialize, tools/list, cross-project init/apply/rollback with clean stdout and single-compose fullName identity across tools.

Current state, verified in the code: `Env.BuildDefault` builds one config from `cwd/migrondi.json`, env vars, and CLI overrides, and one `IMigrondi` from `MigrondiFactory`. That is the whole binding the CLI server needs.

Steps:

1. Prove clean stdout with a spike first. The GUI host has this defect today (deferred to the GUI rework, out of scope here); the CLI host must not copy it. Build the smallest `mcp` stub that answers `initialize` with all logs on stderr, and pass a strict client against it before any tool work starts.
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
- Phase 2 runtime check: in a temp folder with a SQLite database, run the phase 2 Done-when sequence as separate CLI invocations (`init`, `new --up/--down` twice, `list --pending`, `up --dry`, `up --to`, `up`, `down --to`, `status`, `down`) and assert the applied state after each step, including the non-zero `status` exit for an unknown name. Then run `muse skills validate` on both new skills.
- Phase 2.5 runtime check: in the test host, drive the shared HTTP server with a real MCP client through `initialize`, `tools/list`, a dry run, an apply, and a rollback; confirm `Mcp-Session-Id` is issued only on the session-creating response and a non-localhost `Origin` is rejected. Diff `tools/list` against the previous run: only `idempotentHint`/`openWorldHint` may differ.
- Phase 3 runtime check: start `migrondi mcp` from a folder that is not the project folder, with only `--config` in the client entry. Complete one apply and one rollback.
- Phase 4 runtime check: open the GUI on a real display and run the project flow by hand. A build and the headless suite do not count as proof for this phase.
- Phase 5 runtime check: run the same client script against the CLI host and the GUI host and compare the tool results.

## Risks and Non-Goals

- Navs.Avalonia stays at 1.0.0 for this roadmap. Version 2 changes the routing API and removes helpers the GUI uses. That move is GUI work and sits outside these phases.
- The shared project must not grow a project registry to make phase 3 easy. A registry in the shared layer pulls the GUI database into the CLI path and breaks the single-project binding.
- Tool result shapes may change only in phase 1 if a shape names a GUI type. Any other shape change needs a note in the changelog under `Unreleased`.
- No new GUI features in these phases. GUI work here is extraction and segmentation only.
- No multi-project CLI server in these phases. One agent client starts one CLI server per project.
- Phase 2.5 changes only `src/Migrondi.Mcp` and its tests. No GUI app code changes; the GUI host's stdout logging defect is fixed when the GUI is reworked, and until then the multi-project server is exercised through the test host.
- No fused CLI workflows in phase 2. Flags make each existing command complete on its own; sequencing stays with the agent, and multi-step operations stay with the MCP tools.
