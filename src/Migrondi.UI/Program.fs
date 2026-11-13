module Migrondi.UI.Program

open Goo
open Microsoft.Extensions.Logging

open Navs
open Navs.Goo

open MigrondiUI
open MigrondiUI.Services
open Migrondi.UI
open Migrondi.UI.Feedback
open Migrondi.UI.Host
open Migrondi.UI.Mcp
open Migrondi.UI.Types
open Migrondi.UI.Views

let runGui() =
  Window.ConfigureApplication(
    "Migrondi UI",
    "0.1.0",
    "io.github.angelmunoz.migrondi.ui.spike"
  )

  use loggerFactory =
    LoggerFactory.Create(fun builder -> builder.AddConsole() |> ignore)

  // Migrate the app's own database before any service reads it.
  Migrations.GetMigrondi loggerFactory
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")
  |> Migrations.Migrate

  let marshaller = UiMarshaller()

  let themes = ThemeController { Dark = true }

  let projects =
    Services.ProjectCollection(
      loggerFactory.CreateLogger(),
      Database.ConnectionFactory
    )
    :> IProjectCollection

  let migrondiFactory =
    Services.MigrationOperationsFactory(
      loggerFactory,
      Database.ConnectionFactory
    )
    :> IMigrationOperationsFactory

  let feedback = Feedback.create(marshaller :> IPostUI)
  let filePicker = FilePicker()
  let navigator = GooNavigator()

  let env = {
    new IAppEnv with
      member _.Projects = projects
      member _.Migrations = migrondiFactory
      member _.PostUI = marshaller :> IPostUI
      member _.Theme = themes :> IThemeController
      member _.Feedback = feedback :> IFeedback
      member _.FilePicker = filePicker :> Migrondi.UI.Logic.IFilePicker
      member _.Navigator = (navigator :> IProvideNavigator).Navigator
  }

  let shell = Shell.create(env, navigator, feedback :> Cell)

  let window =
    Window(Title = "Migrondi UI", Width = 1024, Height = 640, Root = shell)

  marshaller.Attach(window)
  feedback.Attach(window)
  filePicker.Attach(window)

  // The outlet the shell mounts needs the window, so change notices from
  // background-thread routes can post to the UI thread.
  shell.Outlet.Window <- Some window

  // Widgets take the OS appearance (and follow it live); the app palette
  // takes the same snapshot. An OS change afterwards re-syncs both through
  // the controller and repaints the pages.
  themes.Attach(window)

  window.add_PreferencesChanged(fun _ -> shell.Refresh())

  // Subscriptions come last: the initial load posts after the window
  // exists, so nothing is dropped.
  shell.Start()

  window.Run()

[<EntryPoint>]
let main argv =
  // The server mode parses before any Goo configuration: in MCP mode there
  // is no window, no Goo calls, and every log line goes to stderr.
  match Server.tryParseArgs argv with
  | Some { mode = Migrondi.Mcp.McpMode.Stdio } ->
    // The GUI serves HTTP only; stdio belongs to the migrondi CLI.
    eprintfn "The GUI does not serve stdio; run `migrondi mcp --stdio` instead."
    2
  | Some options ->
#if DEBUG
    let loggerFactory =
      Migrondi.Mcp.McpLogging.createLoggerFactory LogLevel.Debug
#else
    let loggerFactory =
      Migrondi.Mcp.McpLogging.createLoggerFactory LogLevel.Information
#endif

    Server.runMcpServer Database.ConnectionFactory options loggerFactory
    |> Async.RunSynchronously

    0
  | None ->
    runGui()
    0
