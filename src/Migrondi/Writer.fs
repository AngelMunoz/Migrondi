namespace Migrondi.Writer

open System.Text.Json
open System.Text.Json.Serialization
open Spectre.Console


type ConsoleOutput =
    | Normal of string
    | Warning of string
    | Danger of string
    | Success of string

type JsonOutput =
    { FullContent: string
      Parts: ConsoleOutput list }

    static member FromParts(parts: ConsoleOutput list) =
        { FullContent = System.String.Join("", parts)
          Parts = parts }

type MigrondiOutput =
    | ConsoleOutput of ConsoleOutput list
    | JsonOutput of JsonOutput

type JsonWriter = JsonOutput -> string
type ConsoleWriter = ConsoleOutput list -> string

type MigrondiWriter = MigrondiOutput -> string

[<RequireQualifiedAccess>]
module Writer =

    let Writer
        (jsonWriter: JsonOutput -> string)
        (consoleWriter: ConsoleOutput list -> string)
        (output: MigrondiOutput)
        =
        match output with
        | JsonOutput output -> jsonWriter output
        | ConsoleOutput output -> consoleWriter output

    let private jsonOptions =
        lazy
            (fun () ->
                let opts = JsonSerializerOptions()
                opts.Converters.Add(JsonFSharpConverter())
                opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
                opts)

    let private jsonWriter: JsonWriter =
        fun json -> JsonSerializer.Serialize(json, jsonOptions.Value())

    let private coloredConsoleWriter: ConsoleWriter =
        fun parts ->
            let colored =
                parts
                |> List.map
                    (function
                    | Normal value -> value
                    | Warning value -> $"[yellow]{value}[/]"
                    | Danger value -> $"[red]{value}[/]"
                    | Success value -> $"[green]{value}[/]")

            System.String.Join("", colored)

    let private noColorConsoleWriter: ConsoleWriter =
        fun parts ->
            let colored =
                parts
                |> List.map
                    (function
                    | Normal value
                    | Warning value
                    | Danger value
                    | Success value -> value)

            System.String.Join("", colored)

    let GetMigrondiWriter (withColor: bool) : MigrondiWriter =
        if withColor then
            Writer jsonWriter coloredConsoleWriter
        else
            Writer jsonWriter noColorConsoleWriter

type MigrondiConsole() =
    static member Log(output: MigrondiOutput, ?withColor: bool, ?withWriter: MigrondiWriter) =
        let withColor = defaultArg withColor true

        let writer =
            defaultArg withWriter (Writer.GetMigrondiWriter withColor)

        let value =
            match output with
            | JsonOutput _ -> writer output |> Markup.Escape
            | ConsoleOutput _ -> writer output

        AnsiConsole.Markup(value)

[<AutoOpen>]
module BuilderCE =
    type MigrondiOutputListBuilder() =
        member _.Yield(_) : ConsoleOutput list = []

        member _.Run(state: ConsoleOutput list) = state |> List.rev

        [<CustomOperation("normal")>]
        member _.Normal(state: ConsoleOutput list, value: string) = ConsoleOutput.Normal value :: state

        [<CustomOperation("warning")>]
        member _.Warning(state: ConsoleOutput list, value: string) = ConsoleOutput.Warning value :: state

        [<CustomOperation("danger")>]
        member _.Danger(state: ConsoleOutput list, value: string) = ConsoleOutput.Danger value :: state

        [<CustomOperation("success")>]
        member _.Success(state: ConsoleOutput list, value: string) = ConsoleOutput.Success value :: state

    let migrondiOutput = MigrondiOutputListBuilder()

    [<RequireQualifiedAccess>]
    module MigrondiOutput =
        let toOutput isJson content =
            if isJson then
                JsonOutput.FromParts content |> JsonOutput
            else
                ConsoleOutput content
