open System

open Avalonia
open Avalonia.Controls
open NXUI.Extensions

open MigrondiUI

let BuildMainWindow(_: Navs.IRouter<_>) = Window().Width(300).Height 300

let ApplyMigrations migrondi = Migrations.Migrate migrondi

let CreateRouter(_: Projects.IProjectRepository) =
  let router: Navs.IRouter<_> = Navs.Avalonia.AvaloniaRouter([])

  router

let CreateProjectRepository() =
  Projects.GetRepository Database.ConnectionFactory

let GetMigrondi() =
  Migrations.GetMigrondi()
  |> ValueOption.defaultWith(fun () -> failwith "No migrondi found")


let Orchestrate() =
  GetMigrondi() |> ApplyMigrations

  CreateProjectRepository() |> CreateRouter |> BuildMainWindow

[<EntryPoint; STAThread>]
let main argv =

  AppBuilder
    .Configure<Application>()
    .UsePlatformDetect()
    .UseFluentTheme(Styling.ThemeVariant.Default)
    .WithApplicationName("NXUI and F#")
    .StartWithClassicDesktopLifetime(Orchestrate, argv)
