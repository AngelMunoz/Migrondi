module MigrondiUI.Program

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

let loggerFactory = (new LoggerFactory()).AddSerilog Log.Logger

let BuildMainWindow(routes: Routes) =
  let win =
    Window().Content(routes).MinWidth(800).MinHeight(640).Title "MigrondiUI"
#if DEBUG
  win.AttachDevTools()
#endif
  win

let Orchestrate() =
  // This is for the current application's database
  // each project is dealt with accordingly
  Migrations.GetMigrondi loggerFactory
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
  |> Migrations.Migrate

  let lProjects, vProjects = Projects.GetRepositories Database.ConnectionFactory

  let migrondiui = MigrondiExt.getMigrondiUI(loggerFactory, vProjects)

  let vfs =
    let logger = loggerFactory.CreateLogger<VirtualFs.MigrondiUIFs>()
    VirtualFs.getVirtualFs(logger, vProjects)

  (lProjects, vProjects, vfs, migrondiui)
  |> Views.Routes.GetRoutes loggerFactory
  |> BuildMainWindow


type App() as this =
  inherit Application()

  do this.RequestedThemeVariant <- Styling.ThemeVariant.Default

  override this.Initialize() =
    this.Styles.Add(Themes.Fluent.FluentTheme())
    this.Styles.Load("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml")
    this.Styles.Add(Semi.Avalonia.SemiTheme())

[<EntryPoint; STAThread>]
let main argv =

  AppBuilder
    .Configure<App>()
    .UsePlatformDetect()
    .StartWithClassicDesktopLifetime(Orchestrate, argv)
