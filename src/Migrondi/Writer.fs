namespace Migrondi.Writer

open System.Text.Json
open System.Text.Json.Serialization
open Spectre.Console
open Migrondi.Types
open System


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
module Json =
    let private defaultOptions =
        lazy
            (let opts = JsonSerializerOptions()
             opts.Converters.Add(JsonFSharpConverter())
             opts.AllowTrailingCommas <- true
             opts.ReadCommentHandling <- JsonCommentHandling.Skip
             opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
             opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
             opts.WriteIndented <- true
             opts)

    let private diskOptions =
        lazy
            (let opts = JsonSerializerOptions()
             opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
             opts.WriteIndented <- true
             opts)

    let defaultJsonWriter: JsonWriter =
        fun json -> JsonSerializer.Serialize(json, defaultOptions.Value)

    let serializeBytes content (indented: bool) =
        JsonSerializer.SerializeToUtf8Bytes(
            content,
            if indented then
                diskOptions.Value
            else
                defaultOptions.Value
        )

    let tryDeserializeFile (bytes: byte array) =
        try
            JsonSerializer.Deserialize<MigrondiConfig>(ReadOnlySpan(bytes), defaultOptions.Value)
            |> Ok
        with
        | ex -> Error ex

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
            Writer Json.defaultJsonWriter coloredConsoleWriter
        else
            Writer Json.defaultJsonWriter noColorConsoleWriter

type MigrondiConsole() =
    static member Log(output: ConsoleOutput list, ?withColor: bool, ?isJson: bool, ?withWriter: MigrondiWriter) =
        let withColor = defaultArg withColor true
        let isJson = defaultArg isJson false

        let output =
            if isJson then
                JsonOutput.FromParts output |> JsonOutput
            else
                ConsoleOutput output

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

        member _.Source(s: ConsoleOutput list) = s

        [<CustomOperation("normal")>]
        member _.Normal(state: ConsoleOutput list, value: string) = ConsoleOutput.Normal value :: state

        [<CustomOperation("normalln")>]
        member _.NormalLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Normal $"{value}{System.Environment.NewLine}"
            :: state

        [<CustomOperation("warning")>]
        member _.Warning(state: ConsoleOutput list, value: string) = ConsoleOutput.Warning value :: state

        [<CustomOperation("warningln")>]
        member _.WarningLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Warning $"{value}{System.Environment.NewLine}"
            :: state

        [<CustomOperation("danger")>]
        member _.Danger(state: ConsoleOutput list, value: string) = ConsoleOutput.Danger value :: state

        [<CustomOperation("dangerln")>]
        member _.DangerLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Danger $"{value}{System.Environment.NewLine}"
            :: state

        [<CustomOperation("success")>]
        member _.Success(state: ConsoleOutput list, value: string) = ConsoleOutput.Success value :: state

        [<CustomOperation("successln")>]
        member _.SuccessLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Success $"{value}{System.Environment.NewLine}"
            :: state

    let migrondiOutput = MigrondiOutputListBuilder()
