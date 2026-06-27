// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace Crdt;

public sealed partial class FugueSequence<T>
{
    /// <summary>Serializes the sequence to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var ids = new List<Dot>(_nodes.Keys);
        ids.Sort();
        var deleted = new List<Dot>(_deleted);
        deleted.Sort();

        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("replica", _replica.Value);
            writer.WriteStartArray("nodes");
            foreach (Dot id in ids)
            {
                Node node = _nodes[id];
                writer.WriteStartObject();
                writer.WriteString("id", DotText(id));
                writer.WriteString("parentId", DotText(node.ParentId));
                writer.WriteString("side", node.Side == FugueSide.Left ? "left" : "right");
                writer.WriteBoolean("deleted", node.Deleted);
                writer.WritePropertyName("value");
                serializer.WriteJson(writer, node.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("deleted");
            foreach (Dot dot in deleted)
            {
                writer.WriteStringValue(DotText(dot));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a sequence from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded sequence.</returns>
    public static FugueSequence<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var sequence = new FugueSequence<T>(new ReplicaId(root.GetProperty("replica").GetGuid()));

        foreach (JsonElement nodeJson in root.GetProperty("nodes").EnumerateArray())
        {
            Dot id = ParseDot(nodeJson.GetProperty("id").GetString()!);
            Dot parentId = ParseDot(nodeJson.GetProperty("parentId").GetString()!);
            FugueSide side = ParseSide(nodeJson.GetProperty("side").GetString()!);
            bool deleted = nodeJson.GetProperty("deleted").GetBoolean();
            JsonElement valueElement = nodeJson.GetProperty("value");
            var reader = new Utf8JsonReader(GetRawValueBytes(valueElement));
            reader.Read();
            T value = serializer.ReadJson(ref reader);
            sequence._nodes[id] = new Node(parentId, side, value, deleted);
            sequence._version.Observe(id);
            if (deleted)
            {
                sequence._deleted.Add(id);
            }
        }

        foreach (JsonElement dot in root.GetProperty("deleted").EnumerateArray())
        {
            sequence.MarkDeleted(ParseDot(dot.GetString()!));
        }

        return sequence;
    }

    private static string DotText(Dot dot) => $"{dot.Replica.Value:N}:{dot.Sequence}";

    private static Dot ParseDot(string text)
    {
        int separator = text.LastIndexOf(':');
        var replica = new ReplicaId(SpanCompat.ParseGuidExactN(text.AsSpan(0, separator)));
        ulong sequence = SpanCompat.ParseUInt64Invariant(text.AsSpan(separator + 1));
        return new Dot(replica, sequence);
    }

    private static FugueSide ParseSide(string text) =>
        string.Equals(text, "left", StringComparison.Ordinal) ? FugueSide.Left : FugueSide.Right;

    private static byte[] GetRawValueBytes(JsonElement element)
    {
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            element.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }
}
