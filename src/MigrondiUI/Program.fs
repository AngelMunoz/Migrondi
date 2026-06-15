module MigrondiUI.Program

open System

open Serilog

open Avalonia
open Avalonia.Controls
open NXUI.Extensions

open Navs.Avalonia

open MigrondiUI
open Microsoft.Extensions.Logging
open MigrondiUI.Mcp

open SukiUI
open SukiUI.Controls
open SukiUI.Enums
open SukiUI.Dialogs
open SukiUI.Toasts
open MigrondiUI.Views.Routes

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

let BuildMainWindow(dialogManager, toastManager) =
  let win =
    (new SukiWindow())
      .BackgroundStyle(SukiBackgroundStyle.Bubble)
      .Hosts(
        SukiDialogHost(Manager = dialogManager),
        SukiToastHost(Manager = toastManager)
      )
      .MinWidth(800)
      .MinHeight(640)
      .Title
      "MigrondiUI"

  win

let Orchestrate() =
  Migrations.GetMigrondi loggerFactory
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
  |> Migrations.Migrate

  let projects =
    Services.ProjectCollection(
      loggerFactory.CreateLogger(),
      Database.ConnectionFactory
    )

  let migrondiFactory =
    Services.MigrationOperationsFactory(
      loggerFactory,
      Database.ConnectionFactory
    )

  let dialogManager: ISukiDialogManager = SukiDialogManager()
  let toastManager: ISukiToastManager = new SukiToastManager()

  let window = BuildMainWindow(dialogManager, toastManager)

  let host =
    MigrondiUIAppHost {
      lf = loggerFactory
      projects = projects
      migrondiFactory = migrondiFactory
      dialogManager = dialogManager
      toastManager = toastManager
      window = window
    }

  window.Content host :> Window


type App() as this =
  inherit Application()

  do this.RequestedThemeVariant <- Styling.ThemeVariant.Default

  override this.Initialize() =
    this.Styles.Add(Themes.Fluent.FluentTheme())
    this.Styles.Load "avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"
    this.Styles.Add(SukiTheme(ThemeColor = SukiColor.Blue))

[<EntryPoint; STAThread>]
let main argv =
  match Server.tryParseArgs argv with
  | Some mcpOptions ->
    Server.runMcpServer Database.ConnectionFactory mcpOptions loggerFactory
    |> Async.RunSynchronously

    0
  | None ->
    AppBuilder
      .Configure<App>()
      .UsePlatformDetect()
      .StartWithClassicDesktopLifetime(Orchestrate, argv)
