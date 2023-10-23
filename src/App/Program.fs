open System.Reactive.Subjects

open Avalonia
open Avalonia.Data
open Avalonia.Controls

open NXUI.Extensions
open NXUI.FSharp.Extensions

open App


let panelContent (window: Window) =

  let counter = new BehaviorSubject<int> 0

  let counterText =
    counter |> Observable.map(fun count -> $"You clicked %i{count} times")

  let incrementOnClick _ observable =
    observable |> Observable.add(fun _ -> counter.OnNext(counter.Value + 1))

  StackPanel()
    .children(
      Button().content("Click me!!").OnClick(incrementOnClick),
      TextBox().text(window.BindTitle()),
      TextBlock().text(counterText, BindingMode.OneWay)
    )

let view () : Window =
  let window = Window().title("NXUI and F#").width(300).height(300)

  window.content(panelContent window)

[<EntryPoint>]
let main argv =
  AppBuilder
    .Configure<Application>()
    .UsePlatformDetect()
    .UseFluentTheme(Styling.ThemeVariant.Dark)
    .WithApplicationName("NXUI and F#")
    .StartWithClassicDesktopLifetime(view, argv)
