![.NET Core](https://github.com/AngelMunoz/Migrondi/workflows/.NET%20Core/badge.svg?branch=vnext)

# Migrondi

Migrondi is a SQL Migrations tool designed to be simple and execute simple migrations. Write SQL and execute SQL against your database.

Migrondi Runs on the major three platforms on both `x64` and `arm64`.

## Install

### For .NET users

Grab it as a dotnet tool

```
dotnet tool install Migrondi
```

### For Non .NET users

Grab the binary from the releases page or build from source and put it on your `PATH`, that way the command is available globally e.g.

### Linux/OSX

```bash
# you can put this at the end of your ~/.bashrc or ~/.zshrc
# $HOME/Apps/migrondi is a directory where you have downloaded your "Migrondi" binary
export MIGRONDI_HOME="$HOME/Apps/migrondi"
export PATH="$PATH:$MIGRONDI_HOME"
```

### Windows users

You can add it to your powershell profile via `code $profile` (or your preferred editor) and add the following:

```powershell
# you can put this at the end of your $profile file

$env:MIGRONDI_HOME="$HOME/Apps/migrondi"
$env:PATH += ";$env:MIGRONDI_HOME"
```

or add it via the System Properties

- Open the Start Search, type in "SystemPropertiesAdvanced.exe",
- Click the “Environment Variables…” button.
- Edit the PATH env variable and add the location of the migrondi executable.
- You might need to restart your machine for the changes to take effect.

## Quick Usage Reference

```
Description:
  A dead simple SQL migrations runner, apply or rollback migrations at your ease

Usage:
  Migrondi [command] [options]

Options:
  --version       Show version information
  -?, -h, --help  Show help and usage information

Commands:
  init, setup <path>         Creates a migrondi.json file where the comand is invoked or the path provided []
  create, new <name>         This will create a new SQL migration file in the configured directory for migrations
  apply, up <amount>         Runs migrations against the configured database []
  down, rollback <amount>    Runs migrations against the configured database []
  list, show                 Reads migrations files and the database to show what is the current state of the migrations
  show-state, status <name>  Checks whether the migration file has been applied or not to the database
```
