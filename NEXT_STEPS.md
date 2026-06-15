# Next Steps

> Status snapshot as of Jun 15, 2026. Branch: `refactor/unification`, at remote tip `f7d3c9b` plus the `MigrondiExt.fs` dead-code removal in this commit. Clean tree otherwise.
>
> **Trunk is `vnext`, not `main`.** `origin/HEAD` points at `vnext`; the README's install scripts target `vnext`. `main` is stale (see P4).

## Recent arc

| Commit | Event |
|---|---|
| Feb 12 `0f188d4` | **Merge MigrondiUI into monorepo by project reference (#47)** |
| Feb 13 `4eb33ed` | Automated release CI with changelog versioning (#48) |
| Feb 14 `ca6c3fb` | **Add MCP server mode to MigrondiUI (#49)** |
| Feb 15 `4871df5` | Start `IMigrondi` → `IMigrationOperations` unification |
| `920d470` | refactor: extract ReadTools and WriteTools modules (Server.fs organization) |
| `f7d3c9b` | refactor: return domain types from MCP tools — rewrote Tools.fs; expanded McpServer.fs tests 15→37 (now 82 total with UI tests), fixed the `McpWriteTools` compile break, switched tests to typed `Result` assertions |

The MCP C# SDK stable line shipped ten days after the work paused. Current latest is **1.4.0 (Jun 4, 2026)**; the repo is still on `0.8.0-preview.1`.

## Build / test status today

| Project | Build | Tests |
|---|---|---|
| `Migrondi.Core` | clean | — |
| `Migrondi` (CLI) | clean | — |
| `MigrondiUI` | builds (1 warning: `Tmds.DBus.Protocol` 0.21.2 transitive vuln — GHSA-xrw6-gwf8-vvr9) | — |
| `Migrondi.Tests` | clean | 64/64 pass on net8.0 / net9.0 / net10.0 |
| `MigrondiUI.Tests` | clean | **82/82 pass** on net10.0 |

The MCP test compile break that existed at the session start is **resolved on the remote** (`f7d3c9b`). The per-project isolation tests run and pass.

---

## Completed in this pass

- **Removed dead `MigrondiExt.fs`** — the old `IMigrondiUI` / `wrapLocalMigrondi` / `getMigrondiUI` abstraction, superseded by `Services.fs`'s `MigrationOperationsFactory` + `MigrationOperations` and referenced nowhere outside itself. Dropped its `<Compile>` entry from the fsproj. Build and all 82 + 64 tests still green.

---

## P1 — Unification refactor: essentially complete

After removing `MigrondiExt.fs`, the remaining `IMigrondi` references in MigrondiUI are all legitimate and should stay:

| File | Lines | Why it stays |
|---|---|---|
| `Services.fs` | `IMigrationOperations.Core : IMigrondi` + ctor param | The pragmatic seam. 28+ call sites across Views, MCP tools, and tests reach through `ops.Core.*` for `RunUp`/`RunDown`/`DryRun`/`MigrationsList`/`RunNew`/`Initialize`. Folding `IMigrondi`'s surface into `IMigrationOperations` would duplicate the interface for no gain. |
| `Services.fs` | two `CreateLogger<IMigrondi>()` | Logger type parameters; harmless. |
| `Migrations.fs` | `MigrondiFactory` + `Migrate(migrondi: IMigrondi)` | This is MigrondiUI's *own* startup schema runner (the app using Migrondi as a tool for its internal DB), not per-project operations. Correctly separate. |

No further action on the unification itself. (If the `Core` exposure ever feels leaky, the alternative is an explicit narrower interface, but that's a future design call, not pending work.)

---

## P2 — MCP SDK upgrade `0.8.0-preview.1` → `1.4.0` (minimal bump)

**Scope decision:** minimal bump. Keep the hand-rolled `HttpListener` transport in `HttpServer.fs`. Do **not** adopt `ModelContextProtocol.AspNetCore` in this pass.

> **Note:** `f7d3c9b` rewrote the MCP layer. Line numbers below are refreshed against the current tip, but the high-risk *symbols* are unchanged. Also: the MCP tests no longer read `StructuredContent` — they consume typed `Result` values directly from `McpTools.*`, so the SDK-facing `CallToolResult` construction now lives solely in the `Results.fs` mapper, not in test assertions.

### High-risk call sites (expect compile breaks)

| # | Location | Symbol | Risk |
|---|---|---|---|
| 1 | `Server.fs:692,693` | `McpServerPrimitiveCollection<McpServerTool>` | Likely renamed/moved between 0.8 and 1.0 |
| 2 | `Server.fs:711,714` | `McpServerOptions.ServerInfo` / `.ToolCollection` mutable props | Verify both names survive |
| 3 | `Server.fs:551,553` | `McpServerTool.Create(Delegate, McpServerToolCreateOptions(...))` + `ReadOnly`/`Destructive`/`Services`/`Name`/`Title` | `ReadOnly`/`Destructive` may flip to `bool?`; `Services` DI hook may have changed. Many `createTool` call sites in the read/write tool builders. |
| 4 | `Server.fs:551` (the `Create(Delegate, options)` overload) | delegate-based registration | **Biggest semantic risk.** Verify it still accepts `System.Delegate`, still injects the trailing `CancellationToken`, still reflects F# `option` params into the JSON schema. All custom delegate types in `Types.fs` depend on this. |
| 5 | `Server.fs:740` | `StdioServerTransport("migrondi-mcp", loggerFactory)` 2-arg ctor | Name arg may have been dropped (comes from `ServerInfo`) |
| 6 | `HttpServer.fs:18,37` (+ `HandlePostRequestAsync`/`HandleGetRequestAsync` methods) | `StreamableHttpServerTransport` | Most-exposed to churn because `HttpListener` is hand-rolled rather than using the SDK host. HttpServer.fs was not touched by the remote refactors. |

### Medium-risk

- `McpServer.Create` 4-arg overload `(transport, options, loggerFactory, serviceProvider)` — `Server.fs:743`, `HttpServer.fs:40`. May have been reorganized toward DI (`AddMcpServer`).
- `CallToolResult(StructuredContent=, IsError=)` ctor — `Results.fs:522`. The single SDK-facing construction site (the `McpResultMapper.fromEncoder` mapper). Verify `StructuredContent`'s type hasn't shifted to a wrapper/`JsonElement`.

### Low-risk (likely stable)

`McpJsonUtilities.DefaultOptions`, `JsonRpcMessage`, `McpServer.RunAsync`, `DisposeAsync`, `Implementation`.

### Upgrade order

1. Bump `src/MigrondiUI/MigrondiUI.fsproj` `ModelContextProtocol` to `1.4.0`, restore, build.
2. Fix compiler errors top-down using the table above.
3. Add one integration smoke test that constructs `McpServer.Create(...)` with an in-memory stdio transport and invokes one read + one write tool. **Current tests still never spin up the server** — they call `McpTools.*` directly and consume typed `Result`s — so the SDK surface (delegate dispatch, JSON-schema generation, `CancellationToken` injection, transport) is validated only by compilation, not by running. Lock these behaviors in before trusting the bump.

### Out of scope for this pass (worth a later issue)

- Replace `HttpServer.fs`'s hand-rolled `HttpListener` with the official `ModelContextProtocol.AspNetCore` StreamableHTTP host.
- Hard-coded `"Mcp-Session-Id"` header literal (`HttpServer.fs`) — verify against 1.4's session convention.
- `McpServerOptions.ProtocolVersion` and `.Capabilities` are never set explicitly — relies on SDK defaults; watch for silent negotiation changes.

---

## P3 — Other dependency updates

### Version drift (same package, different versions across projects)

| Package | Migrondi | MigrondiUI | Fix |
|---|---|---|---|
| `Serilog.Extensions.Logging` | 10.0.0 | 9.0.2 | align to 10.0.0 |
| `Serilog.Sinks.Console` | 6.1.1 | 6.0.0 | align |

### Docs/samples drift

Three fsx scripts pin `Microsoft.Extensions.Logging.Console` 9.0.0 while the codebase is on 10.0.3:
- `docs/services/filesystem.fsx`
- `docs/examples/fsharp.fsx`
- `samples/scripts/script.fsx`

### Structural

- **No `Directory.Packages.props`** — Central Package Management is not enabled. The drift above is a direct symptom; enabling CPM would prevent it.
- **No `global.json`** — SDK version floats (currently building on 10.0.301). Worth pinning for reproducibility.
- **Pre-releases still in use:** `Navs.Avalonia 1.0.0-rc-008`, `FSharp.SystemCommandLine 2.0.0-beta5.3` — check for stable releases.

> Note: Avalonia packages are intentionally held at their current versions and are out of scope for dependency updates.

---

## P4 — Branch hygiene

- **`main` is stale** — behind `vnext` by the entire MigrondiUI/MCP/release-CI body of work (tip is `193946e chore: bump version`). Decide: fast-forward `main` to `vnext`, repoint `origin/HEAD` officially, or document `vnext` as trunk in the README.
- **Merged local feature branches** — `feat/add-migrondiui`, `feat/automated-releases`, `feat/mcp`, `fix/45`, `fix/docs-reference`. All merged into `vnext`; candidates for deletion.
- **`migrondiui` remote** (`AngelMunoz/MigrondiUI.git`) + stale branches — the standalone repo absorbed in #47. Decide whether to drop the remote.

---

## P5 — Release housekeeping

- **`CHANGELOG.md` `[Unreleased]`** lists the MigrondiUI additions and the `M.E.Logging.Console` 9.0.6 → 10.0.3 bump, but does **not** mention the unification refactor, the MCP tools domain-type rewrite, or the pending MCP upgrade / dep bumps. Update before the next release cut.
- **`PackageVersion` in `build.fsx`** is hardcoded to `1.2.0`; the automated-release workflow (#48) drives versioning from the changelog — confirm the two stay in sync.
