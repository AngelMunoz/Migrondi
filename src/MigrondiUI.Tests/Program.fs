module Program

open Avalonia
open Avalonia.Headless

type TestAppBuilder() =

  static member BuildAvaloniaApp() =
    AppBuilder
      .Configure<MigrondiUI.Program.App>()
      .UseHeadless(AvaloniaHeadlessPlatformOptions())

[<assembly: AvaloniaTestApplication(typeof<TestAppBuilder>)>]
do ()

[<EntryPoint>]
let main _ = 0
