// Copyright (c) marcschier. Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace Crdt;

/// <content>
/// JSON serialization for <see cref="ReplicatedTree"/>.
/// </content>
public sealed partial class ReplicatedTree
{
    /// <summary>Serializes the tree to deterministic JSON.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("replica", _replica.ToString());
            writer.WriteNumber("counter", _counter);
            writer.WritePropertyName("tree");
            writer.WriteStartArray();
            foreach (KeyValuePair<string, (string Parent, string Meta)> pair in OrderedTree())
            {
                writer.WriteStartObject();
                writer.WriteString("child", pair.Key);
                writer.WriteString("parent", pair.Value.Parent);
                writer.WriteString("meta", pair.Value.Meta);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("log");
            writer.WriteStartArray();
            foreach (LogMove move in _log)
            {
                WriteLogMoveJson(writer, move);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a tree from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded tree.</returns>
    public static ReplicatedTree FromJson(string json)
    {
        Throw.IfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ReplicaId replica = ReplicaId.Parse(root.GetProperty("replica").GetString()!);
        ulong counter = root.GetProperty("counter").GetUInt64();
        var tree = new Dictionary<string, (string Parent, string Meta)>(StringComparer.Ordinal);
        foreach (JsonElement entry in root.GetProperty("tree").EnumerateArray())
        {
            string child = entry.GetProperty("child").GetString()!;
            string parent = entry.GetProperty("parent").GetString()!;
            string meta = entry.GetProperty("meta").GetString()!;
            tree.Add(child, (parent, meta));
        }

        var log = new List<LogMove>();
        MoveTimestamp previous = default;
        bool hasPrevious = false;
        foreach (JsonElement entry in root.GetProperty("log").EnumerateArray())
        {
            LogMove move = ReadLogMoveJson(entry);
            if (hasPrevious && move.Timestamp <= previous)
            {
                Throw.InvalidData<ReplicatedTree>("Move log is not in strictly ascending timestamp order.");
            }

            previous = move.Timestamp;
            hasPrevious = true;
            log.Add(move);
            counter = Math.Max(counter, move.Timestamp.Counter);
        }

        return new ReplicatedTree(replica, counter, tree, log);
    }

    private static void WriteLogMoveJson(Utf8JsonWriter writer, LogMove move)
    {
        writer.WriteStartObject();
        writer.WriteNumber("counter", move.Timestamp.Counter);
        writer.WriteString("replica", move.Timestamp.Replica.ToString());
        writer.WriteString("child", move.Child);
        writer.WriteString("oldParent", move.OldParent);
        writer.WriteString("oldMeta", move.OldMeta);
        writer.WriteString("newParent", move.NewParent);
        writer.WriteString("newMeta", move.NewMeta);
        writer.WriteBoolean("skipped", move.Skipped);
        writer.WriteEndObject();
    }

    private static LogMove ReadLogMoveJson(JsonElement entry)
    {
        var timestamp = new MoveTimestamp(
            entry.GetProperty("counter").GetUInt64(),
            ReplicaId.Parse(entry.GetProperty("replica").GetString()!));
        string child = entry.GetProperty("child").GetString()!;
        string? oldParent = GetNullableString(entry.GetProperty("oldParent"));
        string? oldMeta = GetNullableString(entry.GetProperty("oldMeta"));
        string newParent = entry.GetProperty("newParent").GetString()!;
        string newMeta = entry.GetProperty("newMeta").GetString()!;
        bool skipped = entry.GetProperty("skipped").GetBoolean();
        return new LogMove(timestamp, child, oldParent, oldMeta, newParent, newMeta, skipped);
    }

    private static string? GetNullableString(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Null ? null : element.GetString();
    }
}
