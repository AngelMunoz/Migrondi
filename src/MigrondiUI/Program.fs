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

let BuildMainWindow router =

  Window().Content(RouterOutlet().router router).Width(300).Height 300

let Orchestrate() =

  Migrations.GetMigrondi loggerFactory
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
  |> Migrations.Migrate

  Projects.GetRepository Database.ConnectionFactory
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
