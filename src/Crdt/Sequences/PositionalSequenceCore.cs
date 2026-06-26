// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

internal interface ISequencePosition<TPosition> : IComparable<TPosition>, IEquatable<TPosition>
    where TPosition : notnull
{
    Dot Dot { get; }

    void Write(ref CrdtWriter writer);

    void WriteJson(Utf8JsonWriter writer);
}

internal interface ISequencePositionStrategy<TPosition>
    where TPosition : notnull, ISequencePosition<TPosition>
{
    TPosition Allocate(TPosition? left, TPosition? right, Dot dot);

    TPosition Read(ref CrdtReader reader);

    TPosition ReadJson(JsonElement element);
}

internal sealed class PositionalSequenceCore<T, TPosition, TStrategy> :
    IEquatable<PositionalSequenceCore<T, TPosition, TStrategy>>
    where TPosition : notnull, ISequencePosition<TPosition>
    where TStrategy : struct, ISequencePositionStrategy<TPosition>
{
    private readonly Dictionary<TPosition, T> _entries;
    private readonly HashSet<TPosition> _deleted;
    private readonly VersionVector _version;
    private PositionalSequenceCore<T, TPosition, TStrategy>? _delta;

    public PositionalSequenceCore()
    {
        _entries = [];
        _deleted = [];
        _version = new VersionVector();
    }

    private PositionalSequenceCore(
        Dictionary<TPosition, T> entries,
        HashSet<TPosition> deleted,
        VersionVector version)
    {
        _entries = entries;
        _deleted = deleted;
        _version = version;
    }

    public int Count => VisiblePositions().Count;

    public T this[int index] => _entries[VisiblePositionAt(index)];

    public T[] ToArray()
    {
        List<TPosition> positions = VisiblePositions();
        var values = new T[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            values[i] = _entries[positions[i]];
        }

        return values;
    }

    public TPosition Insert(ReplicaId replica, int index, T value)
    {
        List<TPosition> visible = VisiblePositions();
        if (index < 0 || index > visible.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        TPosition? left = index == 0 ? default : visible[index - 1];
        TPosition? right = index == visible.Count ? default : visible[index];
        Dot dot = _version.Increment(replica);
        TPosition position = default(TStrategy).Allocate(left, right, dot);
        _entries[position] = value;
        RecordDelta().AddEntry(position, value);
        return position;
    }

    public TPosition Delete(int index)
    {
        TPosition position = VisiblePositionAt(index);
        _deleted.Add(position);
        RecordDelta()._deleted.Add(position);
        return position;
    }

    public void Merge(PositionalSequenceCore<T, TPosition, TStrategy> other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<TPosition, T> entry in other._entries)
        {
            _entries.TryAdd(entry.Key, entry.Value);
        }

        foreach (TPosition position in other._deleted)
        {
            _deleted.Add(position);
        }

        _version.Merge(other._version);
    }

    public CrdtOrder Compare(PositionalSequenceCore<T, TPosition, TStrategy> other)
    {
        Throw.IfNull(other);
        CrdtOrder entries = CompareSets(HasKeyNotIn(_entries, other._entries), HasKeyNotIn(other._entries, _entries));
        CrdtOrder deleted = CompareSets(HasNotIn(_deleted, other._deleted), HasNotIn(other._deleted, _deleted));
        return Combine(entries, deleted);
    }

    public PositionalSequenceCore<T, TPosition, TStrategy> Clone() =>
        new(new Dictionary<TPosition, T>(_entries), new HashSet<TPosition>(_deleted), _version.Clone());

    public bool TryExtractDelta([MaybeNullWhen(false)] out PositionalSequenceCore<T, TPosition, TStrategy> delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = _delta;
        _delta = null;
        return true;
    }

    public bool ApplyInsert(TPosition position, T value)
    {
        _version.Observe(position.Dot);
        if (_entries.ContainsKey(position))
        {
            return false;
        }

        _entries[position] = value;
        return true;
    }

    public bool ApplyDelete(TPosition position)
    {
        _version.Observe(position.Dot);
        return _deleted.Add(position);
    }

    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        Write(ref writer, serializer);
    }

    public byte[] ToByteArray(ICrdtValueSerializer<T> serializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, serializer);
        return buffer.WrittenSpan.ToArray();
    }

    public void Write(ref CrdtWriter writer, ICrdtValueSerializer<T> serializer)
    {
        List<TPosition> positions = SortedEntryPositions();
        writer.WriteVarUInt64((ulong)positions.Count);
        foreach (TPosition position in positions)
        {
            position.Write(ref writer);
            serializer.Write(ref writer, _entries[position]);
        }

        var deleted = new List<TPosition>(_deleted);
        deleted.Sort();
        writer.WriteVarUInt64((ulong)deleted.Count);
        foreach (TPosition position in deleted)
        {
            position.Write(ref writer);
        }

        _version.Write(ref writer);
    }

    public static PositionalSequenceCore<T, TPosition, TStrategy> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var sequence = new PositionalSequenceCore<T, TPosition, TStrategy>();
        int entryCount = reader.ReadCount();
        for (int i = 0; i < entryCount; i++)
        {
            TPosition position = default(TStrategy).Read(ref reader);
            sequence._entries[position] = serializer.Read(ref reader);
            sequence._version.Observe(position.Dot);
        }

        int deletedCount = reader.ReadCount();
        for (int i = 0; i < deletedCount; i++)
        {
            TPosition position = default(TStrategy).Read(ref reader);
            sequence._deleted.Add(position);
            sequence._version.Observe(position.Dot);
        }

        sequence._version.Merge(VersionVector.Read(ref reader));
        return sequence;
    }

    public string ToJson(ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("entries");
            foreach (TPosition position in SortedEntryPositions())
            {
                writer.WriteStartObject();
                writer.WritePropertyName("position");
                position.WriteJson(writer);
                writer.WritePropertyName("value");
                serializer.WriteJson(writer, _entries[position]);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("deleted");
            var deleted = new List<TPosition>(_deleted);
            deleted.Sort();
            foreach (TPosition position in deleted)
            {
                position.WriteJson(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static PositionalSequenceCore<T, TPosition, TStrategy> FromJson(
        string json,
        ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var sequence = new PositionalSequenceCore<T, TPosition, TStrategy>();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        foreach (JsonElement entry in root.GetProperty("entries").EnumerateArray())
        {
            TPosition position = default(TStrategy).ReadJson(entry.GetProperty("position"));
            JsonElement valueElement = entry.GetProperty("value");
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(valueElement.GetRawText()));
            reader.Read();
            sequence._entries[position] = serializer.ReadJson(ref reader);
            sequence._version.Observe(position.Dot);
        }

        foreach (JsonElement deleted in root.GetProperty("deleted").EnumerateArray())
        {
            TPosition position = default(TStrategy).ReadJson(deleted);
            sequence._deleted.Add(position);
            sequence._version.Observe(position.Dot);
        }

        return sequence;
    }

    public bool Equals(PositionalSequenceCore<T, TPosition, TStrategy>? other)
    {
        if (other is null || other._entries.Count != _entries.Count || other._deleted.Count != _deleted.Count)
        {
            return false;
        }

        foreach (TPosition position in _entries.Keys)
        {
            if (!other._entries.ContainsKey(position))
            {
                return false;
            }
        }

        return _deleted.SetEquals(other._deleted);
    }

    public override bool Equals([NotNullWhen(true)] object? obj) =>
        Equals(obj as PositionalSequenceCore<T, TPosition, TStrategy>);

    public override int GetHashCode() => HashCode.Combine(_entries.Count, _deleted.Count);

    private void AddEntry(TPosition position, T value)
    {
        _entries[position] = value;
        _version.Observe(position.Dot);
    }

    private PositionalSequenceCore<T, TPosition, TStrategy> RecordDelta() =>
        _delta ??= new PositionalSequenceCore<T, TPosition, TStrategy>();

    private TPosition VisiblePositionAt(int index)
    {
        List<TPosition> positions = VisiblePositions();
        if (index < 0 || index >= positions.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        return positions[index];
    }

    private List<TPosition> VisiblePositions()
    {
        List<TPosition> positions = SortedEntryPositions();
        for (int i = positions.Count - 1; i >= 0; i--)
        {
            if (_deleted.Contains(positions[i]))
            {
                positions.RemoveAt(i);
            }
        }

        return positions;
    }

    private List<TPosition> SortedEntryPositions()
    {
        var positions = new List<TPosition>(_entries.Keys);
        positions.Sort();
        return positions;
    }

    private static bool HasKeyNotIn(Dictionary<TPosition, T> source, Dictionary<TPosition, T> other)
    {
        foreach (TPosition position in source.Keys)
        {
            if (!other.ContainsKey(position))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNotIn(HashSet<TPosition> source, HashSet<TPosition> other)
    {
        foreach (TPosition position in source)
        {
            if (!other.Contains(position))
            {
                return true;
            }
        }

        return false;
    }

    private static CrdtOrder CompareSets(bool thisHasExtra, bool otherHasExtra) => (thisHasExtra, otherHasExtra) switch
    {
        (true, true) => CrdtOrder.Concurrent,
        (true, false) => CrdtOrder.Greater,
        (false, true) => CrdtOrder.Less,
        _ => CrdtOrder.Equal,
    };

    private static CrdtOrder Combine(CrdtOrder left, CrdtOrder right)
    {
        if (left == CrdtOrder.Equal)
        {
            return right;
        }

        if (right == CrdtOrder.Equal)
        {
            return left;
        }

        return left == right ? left : CrdtOrder.Concurrent;
    }
}
