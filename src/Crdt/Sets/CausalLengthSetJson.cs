// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>Provides JSON serialization for <see cref="CausalLengthSet{T}"/>.</summary>
public sealed partial class CausalLengthSet<T>
    where T : notnull
{
    /// <summary>Serializes the set to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("entries");
            WriteJsonEntries(writer, serializer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a set from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded set.</returns>
    public static CausalLengthSet<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var set = new CausalLengthSet<T>();
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "entries")
            {
                ReadJsonEntries(ref reader, set, serializer);
            }
            else
            {
                reader.Skip();
            }
        }

        return set;
    }

    private void WriteJsonEntries(Utf8JsonWriter writer, ICrdtValueSerializer<T> serializer)
    {
        writer.WriteStartArray();
        foreach (SerializedEntry entry in SortedEntries(serializer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("element");
            serializer.WriteJson(writer, entry.Element);
            writer.WriteNumber("length", entry.Length);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void ReadJsonEntries(
        ref Utf8JsonReader reader,
        CausalLengthSet<T> set,
        ICrdtValueSerializer<T> serializer)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            T? element = default;
            ulong length = 0UL;
            bool hasElement = false;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? name = reader.GetString();
                reader.Read();
                if (name == "element")
                {
                    element = serializer.ReadJson(ref reader);
                    hasElement = true;
                }
                else if (name == "length")
                {
                    length = reader.GetUInt64();
                }
                else
                {
                    reader.Skip();
                }
            }

            if (!hasElement)
            {
                Throw.InvalidData<int>("Causal-length set JSON entry is missing an element.");
            }

            if (length != 0UL)
            {
                set._lengths[element!] = length;
            }
        }
    }
}
