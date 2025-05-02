open System

open Serilog

open Avalonia
open Avalonia.Controls
open NXUI.Extensions

open Navs.Avalonia

open MigrondiUI
open Microsoft.Extensions.Logging

Log.Logger <-
  LoggerConfiguration()
#if DEBUG
    .MinimumLevel.Debug()
#else
    .MinimumLevel.Information()
#endif
    .WriteTo.Console()
    .CreateLogger()

let loggerFactory = (new LoggerFactory()).AddSerilog(Log.Logger)

let BuildMainWindow(router: Navs.IRouter<_>) =

  Window().Content(RouterOutlet().router router).Width(300).Height 300

let inline ApplyMigrations migrondi = Migrations.Migrate migrondi

let inline CreateProjectRepository() =
  Projects.GetRepository Database.ConnectionFactory

let Orchestrate() =

  Migrations.GetMigrondi loggerFactory
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
  |> ApplyMigrations

  CreateProjectRepository()
  |> Views.Routes.GetRouter loggerFactory
  |> BuildMainWindow

[<EntryPoint; STAThread>]
let main argv =

  AppBuilder
    .Configure<Application>()
    .UsePlatformDetect()
    .UseFluentTheme(Styling.ThemeVariant.Default)
    .WithApplicationName("Migrondi UI")
    .StartWithClassicDesktopLifetime(Orchestrate, argv)
