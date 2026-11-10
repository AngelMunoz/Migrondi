using System.Text.Json.Serialization;

namespace DomainJson;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Domain.Settings))]
public partial class PlainContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = new[] {
        typeof(DomainJsonConverters.OptionConverter<int>),
        typeof(DomainJsonConverters.ListConverter<string>),
        typeof(DomainJsonConverters.ShapeConverter) })]
[JsonSerializable(typeof(Domain.Person))]
[JsonSerializable(typeof(string[]))]
public partial class FSharpContext : JsonSerializerContext
{
}
