[![.NET Core](https://github.com/AngelMunoz/Migrondi/actions/workflows/dotnetcore.yml/badge.svg)](https://github.com/AngelMunoz/Migrondi/actions/workflows/dotnetcore.yml)
![NuGet Version](https://img.shields.io/nuget/vpre/Migrondi?label=Migrondi)
![NuGet Version](https://img.shields.io/nuget/vpre/Migrondi.Core?label=Migrondi.Core)
![NuGet Version](https://img.shields.io/nuget/vpre/MigrondiUI?label=MigrondiUI)

# Migrondi

Migrondi is a SQL migrations tool: write versioned `.sql` files and it tracks which ones a database has applied, then applies pending migrations or rolls applied ones back.

It supports SQLite, SQL Server, PostgreSQL, and MySQL. The CLI runs on Windows, Linux, and macOS (x64 and arm64). The desktop app, Migrondi UI, runs on Windows x64, Linux x64, and macOS arm64, the platforms Goo supports.

## Install

### .NET tools

The CLI installs as `migrondi`; the desktop app and its HTTP MCP host install as `migrondiui`. Migrondi UI needs the .NET 10 runtime; the CLI targets .NET 8, 9, and 10.

```sh
dotnet tool install --global Migrondi
dotnet tool install --global MigrondiUI
```

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

These addresses serve the newest stable release manifest. Scoop checks the SHA-256 before installing, and `scoop update` adopts each new release.

### Linux: Flatpak

```sh
flatpak install --user --bundle https://github.com/AngelMunoz/Migrondi/releases/latest/download/migrondiui-linux-x64.flatpak
```

The bundle installs Migrondi UI and its launcher. Flatpak offers to install the freedesktop runtime from Flathub. Install the bundle from a new release to update the app.

### Binary archives

Download the archive for your platform from the [releases page](https://github.com/AngelMunoz/Migrondi/releases). Each release also carries a `.sha256` file for its archives.

| App | Platforms | Archive |
| --- | --- | --- |
| CLI | `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` | `migrondi-<version>-<rid>.zip` |
| Migrondi UI | `win-x64`, `linux-x64`, `osx-arm64` | `migrondiui-<version>-<rid>.zip` |

The archives are self-contained: they need no .NET installation. Extract the archive and add its folder to `PATH` (the CLI) or start the app from it (the UI).

Migrondi UI keeps its database and virtual project files in your local application data folder, so an app or tool update does not replace them.

Migrondi UI needs Vulkan 1.3. On Linux it also needs a native Wayland session; X11 and XWayland are not supported.

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

## Build

```sh
dotnet restore
dotnet build Migrondi.slnx
dotnet test Migrondi.slnx
```

Publish a self-contained folder for a platform:

```sh
dotnet publish src/Migrondi/Migrondi.fsproj -c Release -r <RID> --self-contained true -o dist
dotnet publish src/Migrondi.UI/Migrondi.UI.fsproj -c Release -r <RID> --self-contained true -o dist
```

The release workflow (`.github/workflows/release.yml`) packs the .NET tools, builds the archives, and publishes the release. See [CONTRIBUTING.md](CONTRIBUTING.md#releasing) for the release checklist.
