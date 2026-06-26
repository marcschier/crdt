// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>Identifies the kind of change described by an <see cref="ORMapOperation{TKey,TValue}"/>.</summary>
public enum ORMapOperationKind
{
    /// <summary>Adds or merges a CRDT value under a fresh dot.</summary>
    Update = 0,

    /// <summary>Removes the observed dots for a key.</summary>
    Remove = 1,
}

/// <summary>
/// Describes an idempotent update or observed-remove operation for an <see cref="ORMap{TKey,TValue}"/>.
/// </summary>
/// <typeparam name="TKey">The map key type.</typeparam>
/// <typeparam name="TValue">The CRDT value type.</typeparam>
public readonly struct ORMapOperation<TKey, TValue>
    where TKey : notnull
{
    /// <summary>Initializes an update operation.</summary>
    /// <param name="key">The updated key.</param>
    /// <param name="dot">The dot assigned to the update.</param>
    /// <param name="value">The CRDT value to merge.</param>
    public ORMapOperation(TKey key, Dot dot, TValue value)
    {
        Throw.IfNull(key);
        Kind = ORMapOperationKind.Update;
        Key = key;
        Dot = dot;
        Value = value;
        RemovedDots = [];
    }

    /// <summary>Initializes a remove operation.</summary>
    /// <param name="key">The removed key.</param>
    /// <param name="removedDots">The observed dots removed by the operation.</param>
    public ORMapOperation(TKey key, IReadOnlyCollection<Dot> removedDots)
    {
        Throw.IfNull(key);
        Throw.IfNull(removedDots);
        Kind = ORMapOperationKind.Remove;
        Key = key;
        Dot = default;
        Value = default;
        RemovedDots = [.. removedDots];
    }

    /// <summary>Gets the operation kind.</summary>
    public ORMapOperationKind Kind { get; }

    /// <summary>Gets the changed key.</summary>
    public TKey Key { get; }

    /// <summary>Gets the assigned dot for update operations.</summary>
    public Dot Dot { get; }

    /// <summary>Gets the CRDT value to merge for update operations.</summary>
    public TValue? Value { get; }

    /// <summary>Gets the observed dots removed by remove operations.</summary>
    public IReadOnlyCollection<Dot> RemovedDots { get; }

    /// <summary>Serializes this operation using the supplied serializers.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueSerializer">The value serializer.</param>
    public void WriteTo(
        IBufferWriter<byte> output,
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueSerializer<TValue> valueSerializer)
    {
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueSerializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        keySerializer.Write(ref writer, Key);
        if (Kind == ORMapOperationKind.Update)
        {
            writer.WriteDot(Dot);
            valueSerializer.Write(ref writer, Value!);
            return;
        }

        writer.WriteVarUInt64((ulong)RemovedDots.Count);
        foreach (Dot dot in RemovedDots)
        {
            writer.WriteDot(dot);
        }
    }

    /// <summary>Serializes this operation to a new byte array using the supplied serializers.</summary>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueSerializer">The value serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueSerializer<TValue> valueSerializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, keySerializer, valueSerializer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes an operation using the supplied serializers.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueSerializer">The value serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static ORMapOperation<TKey, TValue> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueSerializer<TValue> valueSerializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueSerializer);
        var reader = new CrdtReader(data, options);
        var kind = (ORMapOperationKind)reader.ReadByte();
        TKey key = keySerializer.Read(ref reader);
        if (kind == ORMapOperationKind.Update)
        {
            Dot dot = reader.ReadDot();
            return new ORMapOperation<TKey, TValue>(key, dot, valueSerializer.Read(ref reader));
        }

        int count = reader.ReadCount();
        var dots = new Dot[count];
        for (int i = 0; i < count; i++)
        {
            dots[i] = reader.ReadDot();
        }

        return new ORMapOperation<TKey, TValue>(key, dots);
    }
}

/// <summary>
/// An add-wins observed-remove map whose values are themselves merged CRDT values.
/// </summary>
/// <typeparam name="TKey">The key type; must be non-null and have value equality.</typeparam>
/// <typeparam name="TValue">The CRDT value type.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class ORMap<TKey, TValue> :
    IConvergent<ORMap<TKey, TValue>>,
    IDeltaConvergent<ORMap<TKey, TValue>, ORMap<TKey, TValue>>,
    IOperationConvergent<ORMapOperation<TKey, TValue>>,
    IEquatable<ORMap<TKey, TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries;
    private readonly ICrdtValueOps<TValue> _valueOps;
    private readonly DotContext _context;
    private ORMap<TKey, TValue>? _delta;

    /// <summary>Initializes an empty observed-remove map.</summary>
    /// <param name="valueOps">The CRDT value operations and serializer.</param>
    public ORMap(ICrdtValueOps<TValue> valueOps)
    {
        Throw.IfNull(valueOps);
        _valueOps = valueOps;
        _entries = [];
        _context = new DotContext();
    }

    private ORMap(Dictionary<TKey, Entry> entries, DotContext context, ICrdtValueOps<TValue> valueOps)
    {
        _entries = entries;
        _context = context;
        _valueOps = valueOps;
    }

    /// <summary>Gets the number of live keys in the map.</summary>
    public int Count => _entries.Count;

    /// <summary>Gets the live keys in the map.</summary>
    public IReadOnlyCollection<TKey> Keys => _entries.Keys;

    /// <summary>Gets the value for <paramref name="key"/>.</summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The live value.</returns>
    /// <exception cref="KeyNotFoundException">The key is absent.</exception>
    public TValue this[TKey key]
    {
        get
        {
            if (TryGetValue(key, out TValue? value))
            {
                return value;
            }

            throw new KeyNotFoundException("The key is not present in the OR map.");
        }
    }

    /// <summary>Merges <paramref name="valueToMerge"/> into <paramref name="key"/> under a fresh dot.</summary>
    /// <param name="replica">The replica performing the update.</param>
    /// <param name="key">The key to update.</param>
    /// <param name="valueToMerge">The CRDT value to merge into the current value.</param>
    /// <returns>The operation describing the update.</returns>
    public ORMapOperation<TKey, TValue> Update(ReplicaId replica, TKey key, TValue valueToMerge)
    {
        Throw.IfNull(key);
        Dot dot = _context.NextDot(replica);
        Entry entry = GetOrCreateEntry(key);
        entry.Value = _valueOps.Merge(entry.Value, valueToMerge);
        entry.Dots.Add(dot);

        ORMap<TKey, TValue> delta = Delta();
        Entry deltaEntry = delta.GetOrCreateEntry(key);
        deltaEntry.Value = _valueOps.Merge(deltaEntry.Value, valueToMerge);
        deltaEntry.Dots.Add(dot);
        delta._context.Add(dot);

        return new ORMapOperation<TKey, TValue>(key, dot, valueToMerge);
    }

    /// <summary>Removes every currently observed dot for <paramref name="key"/>.</summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>The operation describing the removal.</returns>
    public ORMapOperation<TKey, TValue> Remove(TKey key)
    {
        Throw.IfNull(key);
        var removed = new List<Dot>();
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            removed.AddRange(entry.Dots);
            _entries.Remove(key);
        }

        ORMap<TKey, TValue> delta = Delta();
        foreach (Dot dot in removed)
        {
            if (delta._entries.TryGetValue(key, out Entry? deltaEntry))
            {
                deltaEntry.Dots.Remove(dot);
                if (deltaEntry.Dots.Count == 0)
                {
                    delta._entries.Remove(key);
                }
            }

            delta._context.Add(dot);
        }

        return new ORMapOperation<TKey, TValue>(key, removed);
    }

    /// <summary>Attempts to get the live value for <paramref name="key"/>.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The live value when present.</param>
    /// <returns><see langword="true"/> when the key has a live value.</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        Throw.IfNull(key);
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            value = entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Determines whether <paramref name="key"/> has a live value.</summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> when the key has a live value.</returns>
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    /// <inheritdoc/>
    public void Merge(ORMap<TKey, TValue> other)
    {
        Throw.IfNull(other);
        var keys = new HashSet<TKey>(_entries.Keys, _entries.Comparer);
        foreach (TKey key in other._entries.Keys)
        {
            keys.Add(key);
        }

        foreach (TKey key in keys)
        {
            _entries.TryGetValue(key, out Entry? left);
            other._entries.TryGetValue(key, out Entry? right);
            HashSet<Dot> liveDots = MergeDots(left, right, other._context);
            if (liveDots.Count == 0)
            {
                _entries.Remove(key);
                continue;
            }

            TValue leftValue = left is null ? _valueOps.CreateZero() : left.Value;
            TValue rightValue = right is null ? _valueOps.CreateZero() : right.Value;
            TValue mergedValue = _valueOps.Merge(_valueOps.Clone(leftValue), rightValue);
            _entries[key] = new Entry(mergedValue, liveDots);
        }

        _context.Merge(other._context);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(ORMap<TKey, TValue> other)
    {
        Throw.IfNull(other);
        if (Equals(other))
        {
            return CrdtOrder.Equal;
        }

        ORMap<TKey, TValue> left = Clone();
        left.Merge(other);
        if (left.Equals(other))
        {
            return CrdtOrder.Less;
        }

        ORMap<TKey, TValue> right = other.Clone();
        right.Merge(this);
        return right.Equals(this) ? CrdtOrder.Greater : CrdtOrder.Concurrent;
    }

    /// <inheritdoc/>
    public ORMap<TKey, TValue> Clone()
    {
        var entries = new Dictionary<TKey, Entry>(_entries.Comparer);
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            entries[entry.Key] = entry.Value.Clone(_valueOps);
        }

        return new ORMap<TKey, TValue>(entries, _context.Clone(), _valueOps);
    }

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out ORMap<TKey, TValue> delta)
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

    /// <inheritdoc/>
    public void MergeDelta(ORMap<TKey, TValue> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(ORMapOperation<TKey, TValue> operation)
    {
        if (operation.Kind == ORMapOperationKind.Update)
        {
            if (!_entries.TryGetValue(operation.Key, out Entry? entry))
            {
                if (_context.Contains(operation.Dot))
                {
                    return false;
                }

                entry = GetOrCreateEntry(operation.Key);
            }
            else if (!entry.Dots.Contains(operation.Dot) && _context.Contains(operation.Dot))
            {
                return false;
            }

            bool changed = entry.Dots.Add(operation.Dot);
            entry.Value = _valueOps.Merge(entry.Value, operation.Value!);
            _context.Add(operation.Dot);
            return changed;
        }

        bool removed = false;
        if (_entries.TryGetValue(operation.Key, out Entry? target))
        {
            foreach (Dot dot in operation.RemovedDots)
            {
                removed |= target.Dots.Remove(dot);
                _context.Add(dot);
            }

            if (target.Dots.Count == 0)
            {
                _entries.Remove(operation.Key);
            }
        }
        else
        {
            foreach (Dot dot in operation.RemovedDots)
            {
                _context.Add(dot);
            }
        }

        return removed;
    }

    /// <summary>Serializes the map to the binary format using the supplied key serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="keySerializer">The key serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<TKey> keySerializer)
    {
        Throw.IfNull(keySerializer);
        var writer = new CrdtWriter(output);
        writer.WriteVarUInt64((ulong)_entries.Count);
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            keySerializer.Write(ref writer, entry.Key);
            _valueOps.Write(ref writer, entry.Value.Value);
            writer.WriteVarUInt64((ulong)entry.Value.Dots.Count);
            foreach (Dot dot in SortedDots(entry.Value.Dots))
            {
                writer.WriteDot(dot);
            }
        }

        _context.Write(ref writer);
    }

    /// <summary>Serializes the map to a new byte array using the supplied key serializer.</summary>
    /// <param name="keySerializer">The key serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<TKey> keySerializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, keySerializer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a map from the binary format using the supplied serializers.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueOps">The CRDT value operations and serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded map.</returns>
    public static ORMap<TKey, TValue> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueOps<TValue> valueOps,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueOps);
        var reader = new CrdtReader(data, options);
        var map = new ORMap<TKey, TValue>(valueOps);
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            TKey key = keySerializer.Read(ref reader);
            TValue value = valueOps.Read(ref reader);
            int dotCount = reader.ReadCount();
            var dots = new HashSet<Dot>();
            for (int j = 0; j < dotCount; j++)
            {
                dots.Add(reader.ReadDot());
            }

            map._entries[key] = new Entry(value, dots);
        }

        map._context.Merge(DotContext.Read(ref reader));
        return map;
    }

    /// <summary>Serializes the map to JSON using the supplied key serializer.</summary>
    /// <param name="keySerializer">The key serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<TKey> keySerializer)
    {
        Throw.IfNull(keySerializer);
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("entries");
            WriteJsonEntries(writer, keySerializer);
            writer.WritePropertyName("context");
            WriteJsonContext(writer, _context);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a map from JSON using the supplied serializers.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueOps">The CRDT value operations and serializer.</param>
    /// <returns>The decoded map.</returns>
    public static ORMap<TKey, TValue> FromJson(
        string json,
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueOps<TValue> valueOps)
    {
        Throw.IfNull(json);
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueOps);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var map = new ORMap<TKey, TValue>(valueOps);
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "entries")
            {
                ReadJsonEntries(ref reader, map, keySerializer);
            }
            else if (name == "context")
            {
                ReadJsonContext(ref reader, map._context);
            }
            else
            {
                reader.Skip();
            }
        }

        return map;
    }

    /// <inheritdoc/>
    public bool Equals(ORMap<TKey, TValue>? other)
    {
        if (other is null || _entries.Count != other._entries.Count || !ContextEquals(_context, other._context))
        {
            return false;
        }

        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            if (!other._entries.TryGetValue(entry.Key, out Entry? value) || !EntryEquals(entry.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ORMap<TKey, TValue>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value.Value);
            foreach (Dot dot in entry.Value.Dots)
            {
                hash ^= HashCode.Combine(entry.Key, dot);
            }
        }

        foreach (KeyValuePair<ReplicaId, ulong> entry in _context.CompactEntries())
        {
            hash ^= HashCode.Combine(1, entry.Key, entry.Value);
        }

        foreach (Dot dot in _context.CloudDots())
        {
            hash ^= HashCode.Combine(2, dot);
        }

        return hash;
    }

    private ORMap<TKey, TValue> Delta() => _delta ??= new ORMap<TKey, TValue>(_valueOps);

    private Entry GetOrCreateEntry(TKey key)
    {
        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            entry = new Entry(_valueOps.CreateZero(), []);
            _entries[key] = entry;
        }

        return entry;
    }

    private HashSet<Dot> MergeDots(Entry? left, Entry? right, DotContext otherContext)
    {
        var liveDots = new HashSet<Dot>();
        if (left is not null)
        {
            foreach (Dot dot in left.Dots)
            {
                if ((right is not null && right.Dots.Contains(dot)) || !otherContext.Contains(dot))
                {
                    liveDots.Add(dot);
                }
            }
        }

        if (right is not null)
        {
            foreach (Dot dot in right.Dots)
            {
                if (liveDots.Contains(dot) || (left is not null && left.Dots.Contains(dot)) || !_context.Contains(dot))
                {
                    liveDots.Add(dot);
                }
            }
        }

        return liveDots;
    }

    private void WriteJsonEntries(Utf8JsonWriter writer, ICrdtValueSerializer<TKey> keySerializer)
    {
        writer.WriteStartArray();
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("key");
            keySerializer.WriteJson(writer, entry.Key);
            writer.WritePropertyName("value");
            _valueOps.WriteJson(writer, entry.Value.Value);
            writer.WritePropertyName("dots");
            writer.WriteStartArray();
            foreach (Dot dot in SortedDots(entry.Value.Dots))
            {
                WriteJsonDot(writer, dot);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void ReadJsonEntries(
        ref Utf8JsonReader reader,
        ORMap<TKey, TValue> map,
        ICrdtValueSerializer<TKey> keySerializer)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            TKey? key = default;
            TValue? value = default;
            var dots = new HashSet<Dot>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? name = reader.GetString();
                reader.Read();
                if (name == "key")
                {
                    key = keySerializer.ReadJson(ref reader);
                }
                else if (name == "value")
                {
                    value = map._valueOps.ReadJson(ref reader);
                }
                else if (name == "dots")
                {
                    ReadJsonDots(ref reader, dots);
                }
                else
                {
                    reader.Skip();
                }
            }

            map._entries[key!] = new Entry(value!, dots);
        }
    }

    private static void ReadJsonDots(ref Utf8JsonReader reader, HashSet<Dot> dots)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            dots.Add(ReadJsonDot(ref reader));
        }
    }

    private static void WriteJsonContext(Utf8JsonWriter writer, DotContext context)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("compact");
        writer.WriteStartArray();
        foreach (KeyValuePair<ReplicaId, ulong> entry in context.CompactEntries())
        {
            writer.WriteStartObject();
            writer.WriteString("replica", entry.Key.Value);
            writer.WriteNumber("sequence", entry.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WritePropertyName("cloud");
        writer.WriteStartArray();
        foreach (Dot dot in context.CloudDots())
        {
            WriteJsonDot(writer, dot);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void ReadJsonContext(ref Utf8JsonReader reader, DotContext context)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "compact")
            {
                ReadJsonCompact(ref reader, context);
            }
            else if (name == "cloud")
            {
                ReadJsonCloud(ref reader, context);
            }
            else
            {
                reader.Skip();
            }
        }
    }

    private static void ReadJsonCompact(ref Utf8JsonReader reader, DotContext context)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ReplicaId replica = ReplicaId.Empty;
            ulong sequence = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? name = reader.GetString();
                reader.Read();
                if (name == "replica")
                {
                    replica = new ReplicaId(reader.GetGuid());
                }
                else if (name == "sequence")
                {
                    sequence = reader.GetUInt64();
                }
                else
                {
                    reader.Skip();
                }
            }

            for (ulong i = 1; i <= sequence; i++)
            {
                context.Add(new Dot(replica, i));
            }
        }
    }

    private static void ReadJsonCloud(ref Utf8JsonReader reader, DotContext context)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            context.Add(ReadJsonDot(ref reader));
        }
    }

    private static void WriteJsonDot(Utf8JsonWriter writer, Dot dot)
    {
        writer.WriteStartObject();
        writer.WriteString("replica", dot.Replica.Value);
        writer.WriteNumber("sequence", dot.Sequence);
        writer.WriteEndObject();
    }

    private static Dot ReadJsonDot(ref Utf8JsonReader reader)
    {
        ReplicaId replica = ReplicaId.Empty;
        ulong sequence = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "replica")
            {
                replica = new ReplicaId(reader.GetGuid());
            }
            else if (name == "sequence")
            {
                sequence = reader.GetUInt64();
            }
            else
            {
                reader.Skip();
            }
        }

        return new Dot(replica, sequence);
    }

    private static List<Dot> SortedDots(HashSet<Dot> dots)
    {
        var list = new List<Dot>(dots);
        list.Sort();
        return list;
    }

    private bool EntryEquals(Entry left, Entry right) => _valueOps.AreEqual(left.Value, right.Value)
        && DotSequenceEquals(SortedDots(left.Dots), SortedDots(right.Dots));

    private static bool ContextEquals(DotContext left, DotContext right)
    {
        return CompactEquals(left.CompactEntries(), right.CompactEntries())
            && DotSequenceEquals(left.CloudDots(), right.CloudDots());
    }

    private static bool CompactEquals(
        IEnumerable<KeyValuePair<ReplicaId, ulong>> left,
        IEnumerable<KeyValuePair<ReplicaId, ulong>> right)
    {
        using IEnumerator<KeyValuePair<ReplicaId, ulong>> leftEnumerator = left.GetEnumerator();
        using IEnumerator<KeyValuePair<ReplicaId, ulong>> rightEnumerator = right.GetEnumerator();
        while (leftEnumerator.MoveNext())
        {
            if (!rightEnumerator.MoveNext() || !leftEnumerator.Current.Equals(rightEnumerator.Current))
            {
                return false;
            }
        }

        return !rightEnumerator.MoveNext();
    }

    private static bool DotSequenceEquals(IEnumerable<Dot> left, IEnumerable<Dot> right)
    {
        using IEnumerator<Dot> leftEnumerator = left.GetEnumerator();
        using IEnumerator<Dot> rightEnumerator = right.GetEnumerator();
        while (leftEnumerator.MoveNext())
        {
            if (!rightEnumerator.MoveNext() || leftEnumerator.Current != rightEnumerator.Current)
            {
                return false;
            }
        }

        return !rightEnumerator.MoveNext();
    }

    private sealed class Entry
    {
        public Entry(TValue value, HashSet<Dot> dots)
        {
            Value = value;
            Dots = dots;
        }

        public TValue Value { get; set; }

        public HashSet<Dot> Dots { get; }

        public Entry Clone(ICrdtValueOps<TValue> valueOps) => new(valueOps.Clone(Value), new HashSet<Dot>(Dots));
    }
}
