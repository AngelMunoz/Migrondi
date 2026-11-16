![.NET Core](https://github.com/AngelMunoz/Migrondi/workflows/.NET%20Core/badge.svg?branch=vnext)

# Migrondi

Migrondi is a SQL migrations tool: write versioned `.sql` files and it tracks which ones a database has applied, then applies pending migrations or rolls applied ones back.

It supports SQLite, SQL Server, PostgreSQL, and MySQL. The CLI runs on Windows, Linux, and macOS (x64 and arm64). The desktop app, Migrondi UI, runs on Windows x64, Linux x64, and macOS arm64.

## Install

### .NET tools

```sh
dotnet tool install --global Migrondi
dotnet tool install --global MigrondiUI
```

The CLI installs as `migrondi`; the desktop app and its HTTP MCP host install as `migrondiui`. Both tools target .NET 10 and .NET 11, so a tool install needs a matching .NET 10 or .NET 11 runtime. The `Migrondi.Core` and `Migrondi.Mcp` libraries target .NET 10 only.

Run a tool without installing it:

```sh
dnx Migrondi -- list
dnx MigrondiUI -- --http 8080
```

### Windows: Scoop

```powershell
scoop install https://github.com/AngelMunoz/Migrondi/releases/latest/download/migrondi.json
scoop install https://github.com/AngelMunoz/Migrondi/releases/latest/download/migrondi-ui.json
```

`migrondi.json` installs the CLI command on Windows x64 and ARM64. `migrondi-ui.json` installs the desktop app with a Start-menu shortcut on Windows x64 only; the UI has no Windows ARM64 build.

### Linux: Flatpak

Install from the Flatpak channel to get updates with `flatpak update --user`: the CLI for Linux x64 and ARM64, the UI for Linux x64 only.

```sh
flatpak install --user --from https://angelmunoz.github.io/Migrondi/io.github.angelmunoz.migrondi.flatpakref
flatpak install --user --from https://angelmunoz.github.io/Migrondi/io.github.angelmunoz.migrondi.ui.flatpakref
```

That registers the `migrondi` channel and installs the newest stable release; Flatpak picks the build for your machine's architecture. The CLI installs as `io.github.angelmunoz.migrondi` and provides the `migrondi` command (`flatpak run io.github.angelmunoz.migrondi list`). The UI installs as `io.github.angelmunoz.migrondi.ui` with its desktop launcher.

Direct-install bundles ride each release for one-off installs (`migrondi-linux-<arch>.flatpak` and `migrondiui-linux-x64.flatpak`, plus versioned names); a bundle install gets no updates, so prefer the channel above:

```sh
flatpak install --user --bundle https://github.com/AngelMunoz/Migrondi/releases/latest/download/migrondi-linux-x64.flatpak
```

### Binary archives

The [releases page](https://github.com/AngelMunoz/Migrondi/releases) carries self-contained archives:

| App | Platforms | Archive |
| --- | --- | --- |
| CLI | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` | `migrondi-<version>-<rid>.zip` |
| Migrondi UI | `win-x64`, `linux-x64`, `osx-arm64` | `migrondiui-<version>-<rid>.zip` |

Extract the archive and add its folder to `PATH`. Migrondi UI keeps its database and virtual project files in your local application data folder, so updates do not replace them. Migrondi UI needs Vulkan 1.3; on Linux it also needs a native Wayland session.

## Agent skills

The repository ships two agent skills: `migrondi-cli` (drive the CLI and its MCP server) and `migrondi-library` (embed `Migrondi.Core`). Install them with the [skills](https://github.com/vercel-labs/skills) CLI:

```sh
npx skills add AngelMunoz/Migrondi --skill migrondi-cli --skill migrondi-library
```

Run `npx skills add AngelMunoz/Migrondi --list` to see everything the repository offers first.

## Quick usage reference

```text
Usage:
  Migrondi [command] [options]

Options:
  --version       Show version information
  -?, -h, --help  Show help and usage information

Commands:
  init, setup <path>         Creates a migrondi.json file where the command is invoked or the path provided
  create, new <name>         Creates a new SQL migration file in the configured migrations directory
  apply, up <amount>         Applies pending migrations
  down, rollback <amount>    Rolls applied migrations back
  list, show                 Reads migration files and the database to show the current state
  show-state, status <name>  Checks whether a migration is applied or pending
  mcp                        Runs an MCP server over stdio for the project in the current directory or --config
```
