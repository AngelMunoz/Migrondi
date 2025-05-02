open System

open Avalonia
open Avalonia.Controls
open NXUI.Extensions

open Navs.Avalonia

open MigrondiUI

let BuildMainWindow(router: Navs.IRouter<_>) =

  Window().Content(RouterOutlet().router router).Width(300).Height 300

let inline ApplyMigrations migrondi = Migrations.Migrate migrondi

let inline CreateProjectRepository() =
  Projects.GetRepository Database.ConnectionFactory

let inline GetMigrondi() =
  Migrations.GetMigrondi()
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")

let Orchestrate() =
  GetMigrondi() |> ApplyMigrations

  CreateProjectRepository() |> Views.Routes.GetRouter |> BuildMainWindow

[<EntryPoint; STAThread>]
let main argv =

  AppBuilder
    .Configure<Application>()
    .UsePlatformDetect()
    .UseFluentTheme(Styling.ThemeVariant.Default)
    .WithApplicationName("NXUI and F#")
    .StartWithClassicDesktopLifetime(Orchestrate, argv)
