// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text.Json;

namespace Crdt;

/// <summary>
/// Provides <see cref="ICrdtValueOps{T}"/> support for storing <see cref="GCounter"/> values in maps.
/// </summary>
public sealed class GCounterValueOps : ICrdtValueOps<GCounter>
{
    /// <inheritdoc/>
    public GCounter CreateZero() => new();

    /// <inheritdoc/>
    public GCounter Merge(GCounter current, GCounter other)
    {
        Throw.IfNull(current);
        Throw.IfNull(other);
        current.Merge(other);
        return current;
    }

    /// <inheritdoc/>
    public GCounter Clone(GCounter value)
    {
        Throw.IfNull(value);
        return value.Clone();
    }

    /// <inheritdoc/>
    public bool AreEqual(GCounter left, GCounter right) => left.Equals(right);

    /// <inheritdoc/>
    public bool IsZero(GCounter value) => value.Value == 0UL;

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer, GCounter value)
    {
        Throw.IfNull(value);
        value.Write(ref writer);
    }

    /// <inheritdoc/>
    public GCounter Read(ref CrdtReader reader) => GCounter.Read(ref reader);

    /// <inheritdoc/>
    public void WriteJson(Utf8JsonWriter writer, GCounter value)
    {
        Throw.IfNull(value);
        writer.WriteStartArray();
        foreach (KeyValuePair<ReplicaId, ulong> entry in value.SortedCounts())
        {
            writer.WriteStartObject();
            writer.WriteString("replica", entry.Key.Value);
            writer.WriteNumber("value", entry.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <inheritdoc/>
    public GCounter ReadJson(ref Utf8JsonReader reader)
    {
        var counter = new GCounter();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ReplicaId replica = ReplicaId.Empty;
            ulong value = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? name = reader.GetString();
                reader.Read();
                if (name == "replica")
                {
                    replica = new ReplicaId(reader.GetGuid());
                }
                else if (name == "value")
                {
                    value = reader.GetUInt64();
                }
                else
                {
                    reader.Skip();
                }
            }

            counter.SetCount(replica, value);
        }

        return counter;
    }
}
