module UserInterface

open Spectre.Console

let private ansiConsole = AnsiConsole.Console

let successPrint text =
    text
    |> Markup.Escape
    |> Panel
    |> (fun p -> PanelExtensions.Header(p, "Success!"))
    |> ExpandableExtensions.Expand
    |> (fun p -> HasBorderExtensions.BorderColor(p, Color.Green))
    |> ansiConsole.Write

let failurePrint text =
    text
    |> Markup.Escape
    |> Panel
    |> (fun p -> PanelExtensions.Header(p, "Error"))
    |> ExpandableExtensions.Expand
    |> (fun p -> HasBorderExtensions.BorderColor(p, Color.Red))
    |> ansiConsole.Write