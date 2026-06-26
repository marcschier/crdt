// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>Controls which side wins when an add and remove have the same timestamp.</summary>
public enum LWWElementSetBias
{
    /// <summary>The add wins when timestamps are equal.</summary>
    AddWins = 0,

    /// <summary>The remove wins when timestamps are equal.</summary>
    RemoveWins = 1,
}

/// <summary>Identifies the kind of change described by a <see cref="LWWElementSetOperation{T}"/>.</summary>
public enum LWWElementSetOperationKind
{
    /// <summary>Records an add timestamp for the element.</summary>
    Add = 0,

    /// <summary>Records a remove timestamp for the element.</summary>
    Remove = 1,
}

/// <summary>Describes an idempotent timestamped update for a <see cref="LWWElementSet{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct LWWElementSetOperation<T>
    where T : notnull
{
    /// <summary>Initializes a new <see cref="LWWElementSetOperation{T}"/>.</summary>
    /// <param name="kind">The operation kind.</param>
    /// <param name="element">The updated element.</param>
    /// <param name="timestamp">The update timestamp.</param>
    public LWWElementSetOperation(LWWElementSetOperationKind kind, T element, Timestamp timestamp)
    {
        Kind = kind;
        Element = element;
        Timestamp = timestamp;
    }

    /// <summary>Gets the operation kind.</summary>
    public LWWElementSetOperationKind Kind { get; }

    /// <summary>Gets the updated element.</summary>
    public T Element { get; }

    /// <summary>Gets the update timestamp.</summary>
    public Timestamp Timestamp { get; }

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        serializer.Write(ref writer, Element);
        writer.WriteTimestamp(Timestamp);
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static LWWElementSetOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var kind = (LWWElementSetOperationKind)reader.ReadByte();
        T element = serializer.Read(ref reader);
        return new LWWElementSetOperation<T>(kind, element, reader.ReadTimestamp());
    }
}

/// <summary>
/// A last-writer-wins element set. Adds and removes are stored as per-element timestamps;
/// the larger timestamp decides membership, with a configurable tie-break bias.
/// </summary>
/// <typeparam name="T">The element type; must be non-null and have value equality.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class LWWElementSet<T> :
    IConvergent<LWWElementSet<T>>,
    IDeltaConvergent<LWWElementSet<T>, LWWElementSet<T>>,
    IOperationConvergent<LWWElementSetOperation<T>>,
    IEquatable<LWWElementSet<T>>
    where T : notnull
{
    private readonly Dictionary<T, Timestamp> _adds;
    private readonly Dictionary<T, Timestamp> _removes;
    private LWWElementSet<T>? _delta;

    /// <summary>Initializes an empty add-wins set using the default equality comparer.</summary>
    public LWWElementSet()
        : this(LWWElementSetBias.AddWins)
    {
    }

    /// <summary>Initializes an empty set using the supplied tie-break bias.</summary>
    /// <param name="bias">The tie-break bias for equal add and remove timestamps.</param>
    public LWWElementSet(LWWElementSetBias bias)
        : this(bias, EqualityComparer<T>.Default)
    {
    }

    /// <summary>Initializes an empty set using the supplied bias and equality comparer.</summary>
    /// <param name="bias">The tie-break bias for equal add and remove timestamps.</param>
    /// <param name="comparer">The element equality comparer.</param>
    public LWWElementSet(LWWElementSetBias bias, IEqualityComparer<T> comparer)
    {
        Bias = bias;
        _adds = new Dictionary<T, Timestamp>(comparer);
        _removes = new Dictionary<T, Timestamp>(comparer);
    }

    private LWWElementSet(
        LWWElementSetBias bias,
        Dictionary<T, Timestamp> adds,
        Dictionary<T, Timestamp> removes)
    {
        Bias = bias;
        _adds = adds;
        _removes = removes;
    }

    /// <summary>Gets the bias used when add and remove timestamps are equal.</summary>
    public LWWElementSetBias Bias { get; }

    /// <summary>Gets the number of elements currently present.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (T element in _adds.Keys)
            {
                if (Contains(element))
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>Gets the elements currently present.</summary>
    public IReadOnlyCollection<T> Elements
    {
        get
        {
            var elements = new List<T>();
            foreach (T element in _adds.Keys)
            {
                if (Contains(element))
                {
                    elements.Add(element);
                }
            }

            return elements;
        }
    }

    /// <summary>Determines whether <paramref name="element"/> is currently present.</summary>
    /// <param name="element">The element to test.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool Contains(T element)
    {
        if (!_adds.TryGetValue(element, out Timestamp addTimestamp))
        {
            return false;
        }

        if (!_removes.TryGetValue(element, out Timestamp removeTimestamp))
        {
            return true;
        }

        int order = addTimestamp.CompareTo(removeTimestamp);
        return order > 0 || (order == 0 && Bias == LWWElementSetBias.AddWins);
    }

    /// <summary>Records an add for <paramref name="element"/> at <paramref name="timestamp"/>.</summary>
    /// <param name="element">The element to add.</param>
    /// <param name="timestamp">The add timestamp.</param>
    /// <returns>The add operation to broadcast.</returns>
    public LWWElementSetOperation<T> Add(T element, Timestamp timestamp)
    {
        Throw.IfNull(element);
        AddTimestamp(_adds, element, timestamp, bufferDelta: true);
        return new LWWElementSetOperation<T>(LWWElementSetOperationKind.Add, element, timestamp);
    }

    /// <summary>Records an add for <paramref name="element"/> using <paramref name="clock"/>.</summary>
    /// <param name="element">The element to add.</param>
    /// <param name="clock">The clock used to stamp the add.</param>
    /// <returns>The add operation to broadcast.</returns>
    public LWWElementSetOperation<T> Add(T element, IClock clock)
    {
        Throw.IfNull(clock);
        return Add(element, clock.Now());
    }

    /// <summary>Records a remove for <paramref name="element"/> at <paramref name="timestamp"/>.</summary>
    /// <param name="element">The element to remove.</param>
    /// <param name="timestamp">The remove timestamp.</param>
    /// <returns>The remove operation to broadcast.</returns>
    public LWWElementSetOperation<T> Remove(T element, Timestamp timestamp)
    {
        Throw.IfNull(element);
        AddTimestamp(_removes, element, timestamp, bufferDelta: true);
        return new LWWElementSetOperation<T>(LWWElementSetOperationKind.Remove, element, timestamp);
    }

    /// <summary>Records a remove for <paramref name="element"/> using <paramref name="clock"/>.</summary>
    /// <param name="element">The element to remove.</param>
    /// <param name="clock">The clock used to stamp the remove.</param>
    /// <returns>The remove operation to broadcast.</returns>
    public LWWElementSetOperation<T> Remove(T element, IClock clock)
    {
        Throw.IfNull(clock);
        return Remove(element, clock.Now());
    }

    /// <inheritdoc/>
    public void Merge(LWWElementSet<T> other)
    {
        Throw.IfNull(other);
        MergeMap(_adds, other._adds);
        MergeMap(_removes, other._removes);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(LWWElementSet<T> other)
    {
        Throw.IfNull(other);
        return CombineOrders(CompareMap(_adds, other._adds), CompareMap(_removes, other._removes));
    }

    /// <inheritdoc/>
    public LWWElementSet<T> Clone() => new(
        Bias,
        new Dictionary<T, Timestamp>(_adds, _adds.Comparer),
        new Dictionary<T, Timestamp>(_removes, _removes.Comparer));

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out LWWElementSet<T> delta)
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
    public void MergeDelta(LWWElementSet<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(LWWElementSetOperation<T> operation)
    {
        Dictionary<T, Timestamp> map = operation.Kind == LWWElementSetOperationKind.Add ? _adds : _removes;
        return AddTimestamp(map, operation.Element, operation.Timestamp, bufferDelta: false);
    }

    /// <summary>Serializes the set to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Bias);
        WriteMap(ref writer, _adds, serializer);
        WriteMap(ref writer, _removes, serializer);
    }

    /// <summary>Serializes the set to a new byte array using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<T> serializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, serializer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a set from the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded set.</returns>
    public static LWWElementSet<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var set = new LWWElementSet<T>((LWWElementSetBias)reader.ReadByte());
        ReadMap(ref reader, set._adds, serializer);
        ReadMap(ref reader, set._removes, serializer);
        return set;
    }

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
            writer.WriteString("bias", Bias.ToString());
            writer.WritePropertyName("adds");
            WriteJsonMap(writer, _adds, serializer);
            writer.WritePropertyName("removes");
            WriteJsonMap(writer, _removes, serializer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a set from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded set.</returns>
    public static LWWElementSet<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var adds = new Dictionary<T, Timestamp>();
        var removes = new Dictionary<T, Timestamp>();
        LWWElementSetBias bias = LWWElementSetBias.AddWins;
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "bias")
            {
                bias = Enum.Parse<LWWElementSetBias>(reader.GetString() ?? nameof(LWWElementSetBias.AddWins));
            }
            else if (name == "adds")
            {
                ReadJsonMap(ref reader, adds, serializer);
            }
            else if (name == "removes")
            {
                ReadJsonMap(ref reader, removes, serializer);
            }
            else
            {
                reader.Skip();
            }
        }

        return new LWWElementSet<T>(bias, adds, removes);
    }

    /// <inheritdoc/>
    public bool Equals(LWWElementSet<T>? other) =>
        other is not null && Bias == other.Bias && DictionaryEquals(_adds, other._adds) &&
        DictionaryEquals(_removes, other._removes);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as LWWElementSet<T>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = Bias.GetHashCode();
        foreach (KeyValuePair<T, Timestamp> entry in _adds)
        {
            hash ^= HashCode.Combine(1, entry.Key, entry.Value);
        }

        foreach (KeyValuePair<T, Timestamp> entry in _removes)
        {
            hash ^= HashCode.Combine(2, entry.Key, entry.Value);
        }

        return hash;
    }

    private bool AddTimestamp(
        Dictionary<T, Timestamp> map,
        T element,
        Timestamp timestamp,
        bool bufferDelta)
    {
        if (map.TryGetValue(element, out Timestamp current) && current >= timestamp)
        {
            return false;
        }

        map[element] = timestamp;
        if (bufferDelta)
        {
            Dictionary<T, Timestamp> deltaMap = ReferenceEquals(map, _adds) ? Delta()._adds : Delta()._removes;
            deltaMap[element] = timestamp;
        }

        return true;
    }

    private LWWElementSet<T> Delta() => _delta ??= new LWWElementSet<T>(Bias, _adds.Comparer);

    private static void MergeMap(Dictionary<T, Timestamp> target, Dictionary<T, Timestamp> source)
    {
        foreach (KeyValuePair<T, Timestamp> entry in source)
        {
            if (!target.TryGetValue(entry.Key, out Timestamp current) || entry.Value > current)
            {
                target[entry.Key] = entry.Value;
            }
        }
    }

    private static void WriteMap(
        ref CrdtWriter writer,
        Dictionary<T, Timestamp> map,
        ICrdtValueSerializer<T> serializer)
    {
        writer.WriteVarUInt64((ulong)map.Count);
        foreach (KeyValuePair<T, Timestamp> entry in map)
        {
            serializer.Write(ref writer, entry.Key);
            writer.WriteTimestamp(entry.Value);
        }
    }

    private static void ReadMap(
        ref CrdtReader reader,
        Dictionary<T, Timestamp> map,
        ICrdtValueSerializer<T> serializer)
    {
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            T element = serializer.Read(ref reader);
            map[element] = reader.ReadTimestamp();
        }
    }

    private static void WriteJsonMap(
        Utf8JsonWriter writer,
        Dictionary<T, Timestamp> map,
        ICrdtValueSerializer<T> serializer)
    {
        writer.WriteStartArray();
        foreach (KeyValuePair<T, Timestamp> entry in map)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("element");
            serializer.WriteJson(writer, entry.Key);
            writer.WritePropertyName("timestamp");
            WriteJsonTimestamp(writer, entry.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void ReadJsonMap(
        ref Utf8JsonReader reader,
        Dictionary<T, Timestamp> map,
        ICrdtValueSerializer<T> serializer)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            T? element = default;
            Timestamp timestamp = Timestamp.MinValue;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? name = reader.GetString();
                reader.Read();
                if (name == "element")
                {
                    element = serializer.ReadJson(ref reader);
                }
                else if (name == "timestamp")
                {
                    timestamp = ReadJsonTimestamp(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            map[element!] = timestamp;
        }
    }

    private static void WriteJsonTimestamp(Utf8JsonWriter writer, Timestamp timestamp)
    {
        writer.WriteStartObject();
        writer.WriteNumber("wallClock", timestamp.WallClock);
        writer.WriteNumber("counter", timestamp.Counter);
        writer.WriteString("origin", timestamp.Origin.Value);
        writer.WriteEndObject();
    }

    private static Timestamp ReadJsonTimestamp(ref Utf8JsonReader reader)
    {
        long wallClock = 0;
        ulong counter = 0;
        ReplicaId origin = ReplicaId.Empty;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "wallClock")
            {
                wallClock = reader.GetInt64();
            }
            else if (name == "counter")
            {
                counter = reader.GetUInt64();
            }
            else if (name == "origin")
            {
                origin = new ReplicaId(reader.GetGuid());
            }
            else
            {
                reader.Skip();
            }
        }

        return new Timestamp(wallClock, counter, origin);
    }

    private static CrdtOrder CompareMap(Dictionary<T, Timestamp> left, Dictionary<T, Timestamp> right)
    {
        bool less = false;
        bool greater = false;
        foreach (KeyValuePair<T, Timestamp> entry in left)
        {
            if (!right.TryGetValue(entry.Key, out Timestamp other))
            {
                greater = true;
            }
            else if (entry.Value < other)
            {
                less = true;
            }
            else if (entry.Value > other)
            {
                greater = true;
            }
        }

        foreach (KeyValuePair<T, Timestamp> entry in right)
        {
            if (!left.ContainsKey(entry.Key))
            {
                less = true;
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

    private static CrdtOrder CombineOrders(CrdtOrder left, CrdtOrder right)
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

    private static bool DictionaryEquals(Dictionary<T, Timestamp> left, Dictionary<T, Timestamp> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<T, Timestamp> entry in left)
        {
            if (!right.TryGetValue(entry.Key, out Timestamp timestamp) || timestamp != entry.Value)
            {
                return false;
            }
        }

        return true;
    }
}
