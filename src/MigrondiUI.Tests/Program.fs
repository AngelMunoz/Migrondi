module Program

open Avalonia
open Avalonia.Headless


open Avalonia.Controls.ApplicationLifetimes


type TestAppBuilder() =

  static member BuildAvaloniaApp() =
    AppBuilder
      .Configure<MigrondiUI.Program.App>()
      .UseHeadless(AvaloniaHeadlessPlatformOptions())

[<assembly: AvaloniaTestApplication(typeof<TestAppBuilder>)>]
do ()

[<EntryPoint>]
let main _ = 0
