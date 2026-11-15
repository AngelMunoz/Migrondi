# Contributing

Migrondi doesn't require external dependencies, the only things you need is to have the [dotnet sdk](https://dotnet.microsoft.com/download/visual-studio-sdks) installed on your machine and fork this project from then on, you should be able to modify your code locally and eventually send a pull request.



## Setup
Once the dotnet sdk is installed

```sh
git clone <Your Fork URL>
dotnet restore
dotnet run --project src/Migrondi -- init
```

that will create a local setup of migrondi (a migrations directory and a migrondi.json) which are already ignored by git, depending if you're working on a specific issue of a specific database, you may need to have an existing database of postgresql/mysql/mssql in case your feature is general, you can use sqlite in the `migrondi.json` and a local sql database will be used.

## Running
```sh
dotnet run --project src/Migrondi -- <new or up or any other existing command>
```

## Debugging
If you're using vscode you need to use the [ionide](https://marketplace.visualstudio.com/items?itemName=Ionide.Ionide-fsharp) extension once you have that you will be able to press <kbd>F5</kbd> to run commands in debug mode, please also be aware that there are a couple of pre-existing debugging configurations to feel free to choose from those or add one locally if you need it.

For Visual Studio users, there is a Properties directory with usual VS debug properties, feel free to use those or modify locally to fit your needs.

## Releasing

Releases are cut from `CHANGELOG.md`; the workflow reads the version and release notes from the first released section. The repository keeps `Ionide.KeepAChangelog.Tasks` 0.4.0, the version already pinned in `src/Directory.Packages.props`; it stamps every target framework and rejects a changelog with mixed line endings.

1. Move the `[Unreleased]` entries into a new `## [<version>] - <date>` section and keep `[Unreleased]` in place.
2. Push the change to the release branch (`vnext`) and wait for `.NET Core` to pass.
3. Dispatch the **Create Release** workflow. Every job is skipped when the tag already exists.
4. Verify the release: four NuGet packages (`Migrondi.Core`, `Migrondi.Mcp`, `Migrondi`, `MigrondiUI`), six CLI archives, three UI archives, their `.sha256` sidecars, the two Scoop manifests, and the Flatpak bundle (`migrondiui-<version>-linux-x64.flatpak` and the stable `migrondiui-linux-x64.flatpak`).

NuGet publishing uses trusted publishing. The trusted publisher on nuget.org must name this repository and the `release.yml` workflow; the repository stores the NuGet account name as the `NUGET_USER` secret and no long-lived API key. The workflow requests `id-token: write` and exchanges the GitHub OIDC token with `NuGet/login@v1`.

Check the package output locally before a release:

```sh
dotnet pack src/Migrondi.Core/Migrondi.Core.fsproj -c Release -o nupkgs
dotnet pack src/Migrondi.Mcp/Migrondi.Mcp.fsproj -c Release -o nupkgs
dotnet pack src/Migrondi/Migrondi.fsproj -c Release -o nupkgs
dotnet pack src/Migrondi.UI/Migrondi.UI.fsproj -c Release -o nupkgs
```

The desktop app packs as `MigrondiUI` with the `migrondiui` command. Its own database migrations ride the tool payload under `tools/net10.0/any/migrations`, because a .NET tool package has no content files.

The Flatpak bundle builds from the Linux tarball:

```sh
bash .github/flatpak/build.sh artifacts/migrondiui-<version>-linux-x64.tar.gz
```

This needs `flatpak-builder` and the `org.freedesktop.Sdk//26.08` runtime. The app's database lives in the user's local application data folder, not in `/app`, because Flatpak mounts the app tree read-only.
