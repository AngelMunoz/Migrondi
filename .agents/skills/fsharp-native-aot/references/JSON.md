# F# JSON under Native AOT

Both patterns below round-trip F# records with `option`, `list`, and union fields under
Native AOT with no trim or AOT warnings in user code (net10.0, ILCompiler 10.0.12).
Pattern A is the bundled proof in [../scripts/Domain](../scripts/Domain) and
[../scripts/DomainJson](../scripts/DomainJson).

## Pattern A: C# context plus F# converters

Project layout. References point down the list:

1. `Domain.fsproj` (F#): domain types and converters.
2. `DomainJson.csproj` (C#): only the `JsonSerializerContext`. References `Domain`.
3. `App.fsproj` (F#): references both.

The C# project exists because the System.Text.Json source generator runs only in C#.

### F# converters (in Domain)

```fsharp
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
```

Rules:

- Inside a converter, call the `JsonTypeInfo<'T>` overloads through `options.GetTypeInfo`.
  The `JsonSerializerOptions` overloads carry `RequiresUnreferencedCode`.
- A `ListConverter<'T>` reads through `'T array`, so the context must also list `'T[]`.

### C# context (in DomainJson)

```csharp
using System.Text.Json.Serialization;

namespace DomainJson;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = new[] {
        typeof(DomainJsonConverters.OptionConverter<int>),
        typeof(DomainJsonConverters.ListConverter<string>),
        typeof(DomainJsonConverters.ShapeConverter) })]
[JsonSerializable(typeof(Domain.Person))]
[JsonSerializable(typeof(string[]))]
public partial class FSharpContext : JsonSerializerContext { }
```

Registration rules:

- The generator follows nested records from each `JsonSerializable` root. List roots only, not
  every record.
- Register one closed converter for each distinct option or list element type, for example
  `OptionConverter<int>` and `ListConverter<string>`. The list grows with element types, not
  with record types.
- A missing list converter makes deserialization fail with "The collection type
  'Microsoft.FSharp.Collections.FSharpList`1[...]' is abstract, an interface, or is read only".
- A missing option converter fails silently: `Some x` is written as `{"value":x}`.
- Both failures occur under `dotnet run`. Add a round-trip test for each root type with `Some`
  values and non-empty lists. Assert the JSON text as well as equality, so the `{"value":x}`
  shape fails the test.

An F# module `Domain` in a project that also uses namespace `Domain` gives `FS0247`. Give the
converter module its own top-level name.

### Use (in App)

```fsharp
let ti = DomainJson.FSharpContext.Default.Person
let json = JsonSerializer.Serialize(person, ti)
let back = JsonSerializer.Deserialize(json, ti)
```

## Pattern B: F# only, custom resolver

No C# project. Write one `JsonConverter<T>` per type, here `PersonConverter`, and return its
`JsonTypeInfo` from a resolver.

```fsharp
type DomainResolver() =
    interface IJsonTypeInfoResolver with
        member _.GetTypeInfo(t, options) =
            if t = typeof<Person> then
                JsonMetadataServices.CreateValueInfo<Person>(options, PersonConverter()) :> JsonTypeInfo
            else
                null

let jsonOptions = JsonSerializerOptions(TypeInfoResolver = DomainResolver())
let personInfo = jsonOptions.GetTypeInfo(typeof<Person>) :?> JsonTypeInfo<Person>

let json = JsonSerializer.Serialize(person, personInfo)
```

`JsonMetadataServices.CreateValueInfo` needs options that already have a resolver. Calling it
with `JsonSerializerOptions()` throws "JsonSerializerOptions instance must specify a
TypeInfoResolver setting before being marked as read-only".
