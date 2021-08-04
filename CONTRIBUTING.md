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
