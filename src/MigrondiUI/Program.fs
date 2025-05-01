open System

open Avalonia
open Avalonia.Controls
open NXUI.Extensions

let view() = Window().Width(300).Height 300

[<EntryPoint; STAThread>]
let main argv =
  AppBuilder
    .Configure<Application>()
    .UsePlatformDetect()
    .UseFluentTheme(Styling.ThemeVariant.Default)
    .WithApplicationName("NXUI and F#")
    .StartWithClassicDesktopLifetime(view, argv)
