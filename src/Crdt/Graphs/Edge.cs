// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace Crdt;

/// <summary>Represents a directed edge between two vertices.</summary>
/// <typeparam name="TVertex">The vertex type.</typeparam>
/// <param name="Source">The edge source vertex.</param>
/// <param name="Target">The edge target vertex.</param>
public readonly record struct Edge<TVertex>(TVertex Source, TVertex Target)
    where TVertex : notnull;

internal sealed class EdgeCrdtValueSerializer<TVertex> : ICrdtValueSerializer<Edge<TVertex>>
    where TVertex : notnull
{
    private readonly ICrdtValueSerializer<TVertex> _vertexSerializer;

    public EdgeCrdtValueSerializer(ICrdtValueSerializer<TVertex> vertexSerializer)
    {
        Throw.IfNull(vertexSerializer);
        _vertexSerializer = vertexSerializer;
    }

    public void Write(ref CrdtWriter writer, Edge<TVertex> value)
    {
        _vertexSerializer.Write(ref writer, value.Source);
        _vertexSerializer.Write(ref writer, value.Target);
    }

    public Edge<TVertex> Read(ref CrdtReader reader)
    {
        TVertex source = _vertexSerializer.Read(ref reader);
        TVertex target = _vertexSerializer.Read(ref reader);
        return new Edge<TVertex>(source, target);
    }

    public void WriteJson(Utf8JsonWriter writer, Edge<TVertex> value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("source");
        _vertexSerializer.WriteJson(writer, value.Source);
        writer.WritePropertyName("target");
        _vertexSerializer.WriteJson(writer, value.Target);
        writer.WriteEndObject();
    }

    public Edge<TVertex> ReadJson(ref Utf8JsonReader reader)
    {
        TVertex? source = default;
        TVertex? target = default;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "source")
            {
                source = _vertexSerializer.ReadJson(ref reader);
            }
            else if (name == "target")
            {
                target = _vertexSerializer.ReadJson(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        return new Edge<TVertex>(source!, target!);
    }
}
