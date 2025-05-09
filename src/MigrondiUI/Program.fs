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
  let win =
    Window()
      .Content(RouterOutlet().router router)
      .MinWidth(800)
      .MinHeight(640)
      .Title("MigrondiUI")
#if DEBUG
  win.AttachDevTools()
#endif
  win

let Orchestrate() =

  Migrations.GetMigrondi loggerFactory
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
  |> Migrations.Migrate

  Projects.GetRepository Database.ConnectionFactory
  |> Views.Routes.GetRouter loggerFactory
  |> BuildMainWindow


type App() =
  inherit Application()

  override this.Initialize() =
    this.Styles.Load("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")

[<EntryPoint; STAThread>]
let main argv =

  AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    .UseFluentTheme(Styling.ThemeVariant.Default)
    .StartWithClassicDesktopLifetime(Orchestrate, argv)
