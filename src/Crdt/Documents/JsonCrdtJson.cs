// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace Crdt;

public sealed partial class JsonCrdt
{
    /// <summary>Materializes the current visible document as canonical JSON.</summary>
    /// <returns>A JSON object string with object properties in ordinal key order.</returns>
    public string ToJson()
    {
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            _root.WriteJson(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Creates a fresh JSON CRDT document from a JSON object string.</summary>
    /// <param name="json">The JSON object string.</param>
    /// <returns>A new document containing the parsed JSON value.</returns>
    /// <remarks>
    /// The parsed document is a fresh seed state: every object property and array element gets
    /// newly generated dots, and all register leaves get one shared fresh timestamp.
    /// </remarks>
    public static JsonCrdt FromJson(string json)
    {
        Throw.IfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            Throw.InvalidData<JsonCrdt>("JSON CRDT documents must have an object root.");
        }

        var replica = ReplicaId.New();
        var timestamp = new Timestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 0UL, replica);
        var crdt = new JsonCrdt();
        foreach (JsonProperty property in SortedProperties(document.RootElement))
        {
            crdt.SetKey(
                replica, timestamp, Array.Empty<JsonPathSegment>(), property.Name, LiteralFromJson(property.Value));
        }

        return crdt;
    }

    private static JsonLiteral LiteralFromJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var properties = new List<KeyValuePair<string, JsonLiteral>>();
            foreach (JsonProperty property in SortedProperties(element))
            {
                properties.Add(new KeyValuePair<string, JsonLiteral>(property.Name, LiteralFromJson(property.Value)));
            }

            return JsonLiteral.Object(properties);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var items = new List<JsonLiteral>();
            foreach (JsonElement item in element.EnumerateArray())
            {
                items.Add(LiteralFromJson(item));
            }

            return JsonLiteral.Array(items);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return JsonLiteral.PrimitiveValue(JsonPrimitive.String(element.GetString() ?? string.Empty));
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            return JsonLiteral.PrimitiveValue(JsonPrimitive.Number(element.GetDouble()));
        }

        if (element.ValueKind == JsonValueKind.True)
        {
            return JsonLiteral.PrimitiveValue(JsonPrimitive.Boolean(true));
        }

        if (element.ValueKind == JsonValueKind.False)
        {
            return JsonLiteral.PrimitiveValue(JsonPrimitive.Boolean(false));
        }

        return JsonLiteral.PrimitiveValue(JsonPrimitive.Null);
    }

    private static List<JsonProperty> SortedProperties(JsonElement element)
    {
        var properties = new List<JsonProperty>();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            properties.Add(property);
        }

        properties.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return properties;
    }
}
