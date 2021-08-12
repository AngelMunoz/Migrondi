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

    member this.Value =
        match this with
        | Normal value
        | Warning value
        | Danger value
        | Success value -> value

/// Serves as an envelope to output information to the stdout as a single json object
type JsonOutput =
    { FullContent: string
      Parts: ConsoleOutput list }

    /// convert a list of outputs into a JsonOutput
    static member FromParts(parts: ConsoleOutput list) =
        { FullContent = System.String.Join("", parts |> List.map (fun o -> o.Value))
          Parts = parts }

/// Used to difference between JSON and Text based stdout output
type MigrondiOutput =
    | ConsoleOutput of ConsoleOutput list
    | JsonOutput of JsonOutput

/// A function that will convert a json output into a string
type JsonWriter = JsonOutput -> string
/// A function that will convert a list of console outputs into a single string
type ConsoleWriter = ConsoleOutput list -> string

/// A function that takes a migrondi output and returns a string (used before writing to the stdout)
type MigrondiWriter = MigrondiOutput -> string

[<RequireQualifiedAccess>]
module Json =
    let private defaultOptions =
        lazy
            (let opts = JsonSerializerOptions()
             opts.Converters.Add(JsonFSharpConverter())
             opts.AllowTrailingCommas <- true
             opts.ReadCommentHandling <- JsonCommentHandling.Skip
#if NET5_0 || NETCOREAPP3_1
             opts.IgnoreNullValues <- true
#else
             opts.DefaultIgnoreCondition <- JsonIgnoreCondition.WhenWritingNull
#endif
             opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
             opts)

    let private diskOptions =
        lazy
            (let opts = JsonSerializerOptions()
             opts.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
             opts.WriteIndented <- true
             opts)

    /// Default json serialization mechanism, unless you want to modify the serialization mechanism use this
    let defaultJsonWriter: JsonWriter =
        fun json -> JsonSerializer.Serialize(json, defaultOptions.Value)

    /// <summary>Converts an object into a byte array</summary>
    /// <param name="content">An object to be serialized into bytes</param>
    /// <param name="indented">Write the resulting json as a "pretty" output or a minified string</param>
    let serializeBytes content (indented: bool) =
        JsonSerializer.SerializeToUtf8Bytes(
            content,
            if indented then
                diskOptions.Value
            else
                defaultOptions.Value
        )

    /// Takes a byte array formed from a JSON string and tries to deserialize it into a <see cref="Migrondi.Types.MigrondiConfig">MigrondiConfig</see> object
    let tryDeserializeFile (bytes: byte array) =
        try
            JsonSerializer.Deserialize<MigrondiConfig>(ReadOnlySpan(bytes), defaultOptions.Value)
            |> Ok
        with
        | ex -> Error ex

[<RequireQualifiedAccess>]
module Writer =

    let private DefaultWriter
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
                    (fun o ->
                        match o with
                        | Normal value -> Markup.Escape(value)
                        | Warning value -> $"[yellow]{Markup.Escape(value)}[/]"
                        | Danger value -> $"[red]{Markup.Escape(value)}[/]"
                        | Success value -> $"[green]{Markup.Escape(value)}[/]")

            String.Join("", colored)

    let private noColorConsoleWriter: ConsoleWriter =
        fun parts ->
            let colored =
                parts
                |> List.map (fun o -> o.Value |> Markup.Escape)

            String.Join("", colored)

    /// <summary>Provides a default MigrondiWriter for Stdout operations</summary>
    /// <param name="withColor">Set false to prevent any kind of color in the stdout</param>
    let GetMigrondiWriter (withColor: bool) : MigrondiWriter =
        if withColor then
            DefaultWriter Json.defaultJsonWriter coloredConsoleWriter
        else
            DefaultWriter Json.defaultJsonWriter noColorConsoleWriter

type MigrondiConsole() =
    // Logs information to the stdout, if this doesn't suit your needs you can provide your own writer
    static member Log(output: ConsoleOutput list, ?noColor: bool, ?isJson: bool, ?withWriter: MigrondiWriter) =
        let noColor = defaultArg noColor false
        let isJson = defaultArg isJson false

        let output =
            if isJson then
                JsonOutput.FromParts output |> JsonOutput
            else
                ConsoleOutput output

        let writer =
            defaultArg withWriter (Writer.GetMigrondiWriter(noColor |> not))

        match output with
        | JsonOutput jsonOutput ->
            if jsonOutput.FullContent
               |> String.IsNullOrWhiteSpace then
                ignore ()
            else
                let output = writer output
                printfn $"{output.Trim()}"
        | ConsoleOutput _ -> AnsiConsole.Markup(writer output)

[<AutoOpen>]
module BuilderCE =
    type MigrondiOutputListBuilder() =
        member _.Yield(_) : ConsoleOutput list = []

        member _.Run(state: ConsoleOutput list) = state |> List.rev

        member _.Source(s: ConsoleOutput list) = s

        [<CustomOperation("normal")>]
        /// No particular style will be applied to the string value
        member _.Normal(state: ConsoleOutput list, value: string) = ConsoleOutput.Normal value :: state

        [<CustomOperation("normalln")>]
        /// No particular style will be applied to the string value, but a '\n' be added at the end
        member _.NormalLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Normal $"{value}\n" :: state

        [<CustomOperation("warning")>]
        /// A yelow style will be applied to the string value
        member _.Warning(state: ConsoleOutput list, value: string) = ConsoleOutput.Warning value :: state

        [<CustomOperation("warningln")>]
        /// A yellow style will be applied to the string value and, a '\n' will be added at the end
        member _.WarningLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Warning $"{value}\n" :: state

        [<CustomOperation("danger")>]
        /// A red style will be applied to the string value
        member _.Danger(state: ConsoleOutput list, value: string) = ConsoleOutput.Danger value :: state

        [<CustomOperation("dangerln")>]
        /// A red style will be applied to the string value and, a '\n' will be added at the end
        member _.DangerLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Danger $"{value}\n" :: state

        [<CustomOperation("success")>]
        /// A green style will be applied to the string value
        member _.Success(state: ConsoleOutput list, value: string) = ConsoleOutput.Success value :: state

        [<CustomOperation("successln")>]
        /// A green style will be applied to the string value and, a '\n' will be added at the end
        member _.SuccessLn(state: ConsoleOutput list, value: string) =
            ConsoleOutput.Success $"{value}\n" :: state

    /// A rather simple ConsoleOutput builder
    let migrondiOutput = MigrondiOutputListBuilder()
