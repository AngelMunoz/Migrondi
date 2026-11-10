module DomainJsonConverters

open System.Text.Json
open System.Text.Json.Serialization
open System.Text.Json.Serialization.Metadata
open Domain

let private info<'T> (options: JsonSerializerOptions) =
    options.GetTypeInfo(typeof<'T>) :?> JsonTypeInfo<'T>

type OptionConverter<'T>() =
    inherit JsonConverter<'T option>()
    override _.HandleNull = true
    override _.Read(r, _, options) =
        if r.TokenType = JsonTokenType.Null then None
        else Some(JsonSerializer.Deserialize(&r, info<'T> options))
    override _.Write(w, value, options) =
        match value with
        | None -> w.WriteNullValue()
        | Some v -> JsonSerializer.Serialize(w, v, info<'T> options)

type ListConverter<'T>() =
    inherit JsonConverter<'T list>()
    override _.Read(r, _, options) =
        JsonSerializer.Deserialize(&r, info<'T array> options) |> List.ofArray
    override _.Write(w, value, options) =
        let ti = info<'T> options
        w.WriteStartArray()
        for v in value do
            JsonSerializer.Serialize(w, v, ti)
        w.WriteEndArray()

type ShapeConverter() =
    inherit JsonConverter<Shape>()
    override _.Read(r, _, _) =
        use doc = JsonDocument.ParseValue(&r)
        let e = doc.RootElement
        match e.GetProperty("kind").GetString() with
        | "circle" -> Circle(e.GetProperty("radius").GetDouble())
        | "rect" -> Rect(e.GetProperty("w").GetDouble(), e.GetProperty("h").GetDouble())
        | k -> raise (JsonException($"Unknown Shape kind: {k}"))
    override _.Write(w, value, _) =
        w.WriteStartObject()
        match value with
        | Circle r ->
            w.WriteString("kind", "circle")
            w.WriteNumber("radius", r)
        | Rect(width, height) ->
            w.WriteString("kind", "rect")
            w.WriteNumber("w", width)
            w.WriteNumber("h", height)
        w.WriteEndObject()
