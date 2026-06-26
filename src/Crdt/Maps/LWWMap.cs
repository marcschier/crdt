// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>Describes an assignment or removal produced by an <see cref="LWWMap{TKey,TValue}"/>.</summary>
/// <typeparam name="TKey">The map key type.</typeparam>
/// <typeparam name="TValue">The map value type.</typeparam>
public readonly struct LWWMapOperation<TKey, TValue>
    where TKey : notnull
{
    /// <summary>Initializes a set operation.</summary>
    /// <param name="key">The assigned key.</param>
    /// <param name="value">The assigned value.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    public LWWMapOperation(TKey key, TValue value, Timestamp timestamp)
    {
        Throw.IfNull(key);
        Key = key;
        Value = value;
        Timestamp = timestamp;
        Deleted = false;
    }

    /// <summary>Initializes a remove operation.</summary>
    /// <param name="key">The removed key.</param>
    /// <param name="timestamp">The removal timestamp.</param>
    public LWWMapOperation(TKey key, Timestamp timestamp)
    {
        Throw.IfNull(key);
        Key = key;
        Value = default;
        Timestamp = timestamp;
        Deleted = true;
    }

    private LWWMapOperation(TKey key, TValue? value, Timestamp timestamp, bool deleted)
    {
        Key = key;
        Value = value;
        Timestamp = timestamp;
        Deleted = deleted;
    }

    /// <summary>Gets the key changed by the operation.</summary>
    public TKey Key { get; }

    /// <summary>Gets the assigned value, or <see langword="default"/> for removes.</summary>
    public TValue? Value { get; }

    /// <summary>Gets the operation timestamp.</summary>
    public Timestamp Timestamp { get; }

    /// <summary>Gets a value indicating whether the operation removes the key.</summary>
    public bool Deleted { get; }

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
        keySerializer.Write(ref writer, Key);
        valueSerializer.Write(ref writer, Value!);
        writer.WriteTimestamp(Timestamp);
        writer.WriteBool(Deleted);
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
    public static LWWMapOperation<TKey, TValue> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueSerializer<TValue> valueSerializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueSerializer);
        var reader = new CrdtReader(data, options);
        TKey key = keySerializer.Read(ref reader);
        TValue value = valueSerializer.Read(ref reader);
        Timestamp timestamp = reader.ReadTimestamp();
        bool deleted = reader.ReadBool();
        return new LWWMapOperation<TKey, TValue>(key, value, timestamp, deleted);
    }
}

/// <summary>
/// A last-writer-wins map where each key keeps the value or removal with the greatest timestamp.
/// </summary>
/// <typeparam name="TKey">The key type; must be non-null and have value equality.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class LWWMap<TKey, TValue> :
    IConvergent<LWWMap<TKey, TValue>>,
    IDeltaConvergent<LWWMap<TKey, TValue>, LWWMap<TKey, TValue>>,
    IOperationConvergent<LWWMapOperation<TKey, TValue>>,
    IEquatable<LWWMap<TKey, TValue>>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries;
    private LWWMap<TKey, TValue>? _delta;

    /// <summary>Initializes an empty last-writer-wins map.</summary>
    public LWWMap() => _entries = [];

    private LWWMap(Dictionary<TKey, Entry> entries) => _entries = entries;

    /// <summary>Gets the number of live keys in the map.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (Entry entry in _entries.Values)
            {
                if (!entry.Deleted)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets the live keys in the map.</summary>
    public IReadOnlyCollection<TKey> Keys
    {
        get
        {
            var keys = new List<TKey>();
            foreach (KeyValuePair<TKey, Entry> entry in _entries)
            {
                if (!entry.Value.Deleted)
                {
                    keys.Add(entry.Key);
                }
            }

            return keys;
        }
    }

    /// <summary>Gets the value for <paramref name="key"/>.</summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The live value.</returns>
    /// <exception cref="KeyNotFoundException">The key is absent or removed.</exception>
    public TValue this[TKey key]
    {
        get
        {
            if (TryGetValue(key, out TValue? value))
            {
                return value;
            }

            throw new KeyNotFoundException("The key is not present in the LWW map.");
        }
    }

    /// <summary>Assigns <paramref name="value"/> to <paramref name="key"/> at <paramref name="timestamp"/>.</summary>
    /// <param name="key">The key to assign.</param>
    /// <param name="value">The value to assign.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <returns>The operation describing the assignment.</returns>
    public LWWMapOperation<TKey, TValue> Set(TKey key, TValue value, Timestamp timestamp)
    {
        var operation = new LWWMapOperation<TKey, TValue>(key, value, timestamp);
        Apply(operation);
        BufferDelta(key, new Entry(value, timestamp, false));
        return operation;
    }

    /// <summary>Assigns <paramref name="value"/> using a fresh timestamp from <paramref name="clock"/>.</summary>
    /// <param name="key">The key to assign.</param>
    /// <param name="value">The value to assign.</param>
    /// <param name="clock">The clock used to stamp the assignment.</param>
    /// <returns>The operation describing the assignment.</returns>
    public LWWMapOperation<TKey, TValue> Set(TKey key, TValue value, IClock clock)
    {
        Throw.IfNull(clock);
        return Set(key, value, clock.Now());
    }

    /// <summary>Removes <paramref name="key"/> at <paramref name="timestamp"/>.</summary>
    /// <param name="key">The key to remove.</param>
    /// <param name="timestamp">The removal timestamp.</param>
    /// <returns>The operation describing the removal.</returns>
    public LWWMapOperation<TKey, TValue> Remove(TKey key, Timestamp timestamp)
    {
        var operation = new LWWMapOperation<TKey, TValue>(key, timestamp);
        Apply(operation);
        BufferDelta(key, new Entry(default, timestamp, true));
        return operation;
    }

    /// <summary>Removes <paramref name="key"/> using a fresh timestamp from <paramref name="clock"/>.</summary>
    /// <param name="key">The key to remove.</param>
    /// <param name="clock">The clock used to stamp the removal.</param>
    /// <returns>The operation describing the removal.</returns>
    public LWWMapOperation<TKey, TValue> Remove(TKey key, IClock clock)
    {
        Throw.IfNull(clock);
        return Remove(key, clock.Now());
    }

    /// <summary>Attempts to get the live value for <paramref name="key"/>.</summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The live value when present.</param>
    /// <returns><see langword="true"/> when the key has a live value.</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        Throw.IfNull(key);
        if (_entries.TryGetValue(key, out Entry? entry) && !entry.Deleted)
        {
            value = entry.Value!;
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
    public void Merge(LWWMap<TKey, TValue> other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<TKey, Entry> entry in other._entries)
        {
            if (!_entries.TryGetValue(entry.Key, out Entry? current) || entry.Value.Timestamp > current.Timestamp)
            {
                _entries[entry.Key] = entry.Value.Clone();
            }
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(LWWMap<TKey, TValue> other)
    {
        Throw.IfNull(other);
        bool less = false;
        bool greater = false;
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            Timestamp otherTimestamp = other._entries.TryGetValue(entry.Key, out Entry? value)
                ? value.Timestamp
                : Timestamp.MinValue;
            SetOrderFlags(entry.Value.Timestamp.CompareTo(otherTimestamp), ref less, ref greater);
        }

        foreach (KeyValuePair<TKey, Entry> entry in other._entries)
        {
            if (!_entries.ContainsKey(entry.Key))
            {
                SetOrderFlags(Timestamp.MinValue.CompareTo(entry.Value.Timestamp), ref less, ref greater);
            }
        }

        return (less, greater) switch
        {
            (true, true) => CrdtOrder.Concurrent,
            (true, false) => CrdtOrder.Less,
            (false, true) => CrdtOrder.Greater,
            _ => CrdtOrder.Equal,
        };
    }

    /// <inheritdoc/>
    public LWWMap<TKey, TValue> Clone()
    {
        var entries = new Dictionary<TKey, Entry>(_entries.Comparer);
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            entries[entry.Key] = entry.Value.Clone();
        }

        return new LWWMap<TKey, TValue>(entries);
    }

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out LWWMap<TKey, TValue> delta)
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
    public void MergeDelta(LWWMap<TKey, TValue> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(LWWMapOperation<TKey, TValue> operation)
    {
        if (_entries.TryGetValue(operation.Key, out Entry? current) && operation.Timestamp <= current.Timestamp)
        {
            return false;
        }

        _entries[operation.Key] = new Entry(operation.Value, operation.Timestamp, operation.Deleted);
        return true;
    }

    /// <summary>Serializes the map to the binary format using the supplied serializers.</summary>
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
        writer.WriteVarUInt64((ulong)_entries.Count);
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            keySerializer.Write(ref writer, entry.Key);
            valueSerializer.Write(ref writer, entry.Value.Value!);
            writer.WriteTimestamp(entry.Value.Timestamp);
            writer.WriteBool(entry.Value.Deleted);
        }
    }

    /// <summary>Serializes the map to a new byte array using the supplied serializers.</summary>
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

    /// <summary>Decodes a map from the binary format using the supplied serializers.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueSerializer">The value serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded map.</returns>
    public static LWWMap<TKey, TValue> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueSerializer<TValue> valueSerializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueSerializer);
        var reader = new CrdtReader(data, options);
        var map = new LWWMap<TKey, TValue>();
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            TKey key = keySerializer.Read(ref reader);
            TValue value = valueSerializer.Read(ref reader);
            Timestamp timestamp = reader.ReadTimestamp();
            bool deleted = reader.ReadBool();
            map._entries[key] = new Entry(value, timestamp, deleted);
        }

        return map;
    }

    /// <summary>Serializes the map to JSON using the supplied serializers.</summary>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueSerializer">The value serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueSerializer<TValue> valueSerializer)
    {
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueSerializer);
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (KeyValuePair<TKey, Entry> entry in _entries)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("key");
                keySerializer.WriteJson(writer, entry.Key);
                writer.WritePropertyName("value");
                valueSerializer.WriteJson(writer, entry.Value.Value!);
                LWWRegister<TValue>.WriteTimestampJson(writer, entry.Value.Timestamp);
                writer.WriteBoolean("deleted", entry.Value.Deleted);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a map from JSON using the supplied serializers.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="keySerializer">The key serializer.</param>
    /// <param name="valueSerializer">The value serializer.</param>
    /// <returns>The decoded map.</returns>
    public static LWWMap<TKey, TValue> FromJson(
        string json,
        ICrdtValueSerializer<TKey> keySerializer,
        ICrdtValueSerializer<TValue> valueSerializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(keySerializer);
        Throw.IfNull(valueSerializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var map = new LWWMap<TKey, TValue>();
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            TKey? key = default;
            TValue? value = default;
            Timestamp timestamp = Timestamp.MinValue;
            bool deleted = false;
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
                    value = valueSerializer.ReadJson(ref reader);
                }
                else if (name == "timestamp")
                {
                    timestamp = LWWRegister<TValue>.ReadTimestampJson(ref reader);
                }
                else if (name == "deleted")
                {
                    deleted = reader.GetBoolean();
                }
                else
                {
                    reader.Skip();
                }
            }

            map._entries[key!] = new Entry(value, timestamp, deleted);
        }

        return map;
    }

    /// <inheritdoc/>
    public bool Equals(LWWMap<TKey, TValue>? other)
    {
        if (other is null || _entries.Count != other._entries.Count)
        {
            return false;
        }

        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            if (!other._entries.TryGetValue(entry.Key, out Entry? value) || !entry.Value.Equals(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as LWWMap<TKey, TValue>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (KeyValuePair<TKey, Entry> entry in _entries)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return hash;
    }

    private static void SetOrderFlags(int order, ref bool less, ref bool greater)
    {
        if (order < 0)
        {
            less = true;
        }
        else if (order > 0)
        {
            greater = true;
        }
    }

    private void BufferDelta(TKey key, Entry entry)
    {
        _delta ??= new LWWMap<TKey, TValue>();
        _delta._entries[key] = entry;
    }

    private sealed class Entry : IEquatable<Entry>
    {
        public Entry(TValue? value, Timestamp timestamp, bool deleted)
        {
            Value = value;
            Timestamp = timestamp;
            Deleted = deleted;
        }

        public TValue? Value { get; }

        public Timestamp Timestamp { get; }

        public bool Deleted { get; }

        public Entry Clone() => new(Value, Timestamp, Deleted);

        public bool Equals(Entry? other) => other is not null && Timestamp.Equals(other.Timestamp)
            && Deleted == other.Deleted && EqualityComparer<TValue>.Default.Equals(Value!, other.Value!);

        public override bool Equals(object? obj) => Equals(obj as Entry);

        public override int GetHashCode() => HashCode.Combine(Value, Timestamp, Deleted);
    }
}
