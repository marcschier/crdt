// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>Identifies the kind of change described by a <see cref="TwoPhaseSetOperation{T}"/>.</summary>
public enum TwoPhaseSetOperationKind
{
    /// <summary>Adds the element to the grow-only add set.</summary>
    Add = 0,

    /// <summary>Adds the element to the grow-only remove set.</summary>
    Remove = 1,
}

/// <summary>Describes an idempotent add or remove operation for a <see cref="TwoPhaseSet{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct TwoPhaseSetOperation<T>
    where T : notnull
{
    /// <summary>Initializes a new <see cref="TwoPhaseSetOperation{T}"/>.</summary>
    /// <param name="kind">The operation kind.</param>
    /// <param name="element">The element being added or removed.</param>
    public TwoPhaseSetOperation(TwoPhaseSetOperationKind kind, T element)
    {
        Kind = kind;
        Element = element;
    }

    /// <summary>Gets the operation kind.</summary>
    public TwoPhaseSetOperationKind Kind { get; }

    /// <summary>Gets the element being added or removed.</summary>
    public T Element { get; }

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        serializer.Write(ref writer, Element);
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static TwoPhaseSetOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var kind = (TwoPhaseSetOperationKind)reader.ReadByte();
        return new TwoPhaseSetOperation<T>(kind, serializer.Read(ref reader));
    }
}

/// <summary>
/// A two-phase set (2P-Set): a remove-wins CRDT composed of a grow-only add set and a
/// grow-only tombstone set. Once an element is removed, it cannot become present again.
/// </summary>
/// <typeparam name="T">The element type; must be non-null and have value equality.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class TwoPhaseSet<T> :
    IConvergent<TwoPhaseSet<T>>,
    IDeltaConvergent<TwoPhaseSet<T>, TwoPhaseSet<T>>,
    IOperationConvergent<TwoPhaseSetOperation<T>>,
    IEquatable<TwoPhaseSet<T>>
    where T : notnull
{
    private readonly HashSet<T> _adds;
    private readonly HashSet<T> _removes;
    private TwoPhaseSet<T>? _delta;

    /// <summary>Initializes an empty two-phase set using the default equality comparer.</summary>
    public TwoPhaseSet()
        : this(EqualityComparer<T>.Default)
    {
    }

    /// <summary>Initializes an empty two-phase set using a custom equality comparer.</summary>
    /// <param name="comparer">The element equality comparer.</param>
    public TwoPhaseSet(IEqualityComparer<T> comparer)
    {
        _adds = new HashSet<T>(comparer);
        _removes = new HashSet<T>(comparer);
    }

    private TwoPhaseSet(HashSet<T> adds, HashSet<T> removes)
    {
        _adds = adds;
        _removes = removes;
    }

    /// <summary>Gets the number of elements currently present.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (T element in _adds)
            {
                if (!_removes.Contains(element))
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
            foreach (T element in _adds)
            {
                if (!_removes.Contains(element))
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
    public bool Contains(T element) => _adds.Contains(element) && !_removes.Contains(element);

    /// <summary>Adds <paramref name="element"/> unless it has already been removed.</summary>
    /// <param name="element">The element to add.</param>
    /// <returns>The add operation to broadcast.</returns>
    public TwoPhaseSetOperation<T> Add(T element)
    {
        Throw.IfNull(element);
        if (_adds.Add(element))
        {
            Delta()._adds.Add(element);
        }

        return new TwoPhaseSetOperation<T>(TwoPhaseSetOperationKind.Add, element);
    }

    /// <summary>Removes <paramref name="element"/> permanently.</summary>
    /// <param name="element">The element to remove; it must be currently present.</param>
    /// <returns>The remove operation to broadcast.</returns>
    public TwoPhaseSetOperation<T> Remove(T element)
    {
        Throw.IfNull(element);
        if (!Contains(element))
        {
            throw new InvalidOperationException("Only a currently-present element can be removed.");
        }

        if (_removes.Add(element))
        {
            Delta()._removes.Add(element);
        }

        return new TwoPhaseSetOperation<T>(TwoPhaseSetOperationKind.Remove, element);
    }

    /// <inheritdoc/>
    public void Merge(TwoPhaseSet<T> other)
    {
        Throw.IfNull(other);
        foreach (T element in other._adds)
        {
            _adds.Add(element);
        }

        foreach (T element in other._removes)
        {
            _removes.Add(element);
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(TwoPhaseSet<T> other)
    {
        Throw.IfNull(other);
        return CombineOrders(CompareSet(_adds, other._adds), CompareSet(_removes, other._removes));
    }

    /// <inheritdoc/>
    public TwoPhaseSet<T> Clone() => new(
        new HashSet<T>(_adds, _adds.Comparer),
        new HashSet<T>(_removes, _removes.Comparer));

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out TwoPhaseSet<T> delta)
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
    public void MergeDelta(TwoPhaseSet<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(TwoPhaseSetOperation<T> operation)
    {
        if (operation.Kind == TwoPhaseSetOperationKind.Remove)
        {
            bool added = _adds.Add(operation.Element);
            bool removed = _removes.Add(operation.Element);
            return added || removed;
        }

        return _adds.Add(operation.Element);
    }

    /// <summary>Serializes the set to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        WriteSet(ref writer, _adds, serializer);
        WriteSet(ref writer, _removes, serializer);
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
    public static TwoPhaseSet<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var set = new TwoPhaseSet<T>();
        ReadSet(ref reader, set._adds, serializer);
        ReadSet(ref reader, set._removes, serializer);
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
            writer.WritePropertyName("adds");
            WriteJsonSet(writer, _adds, serializer);
            writer.WritePropertyName("removes");
            WriteJsonSet(writer, _removes, serializer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a set from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded set.</returns>
    public static TwoPhaseSet<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var set = new TwoPhaseSet<T>();
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "adds")
            {
                ReadJsonSet(ref reader, set._adds, serializer);
            }
            else if (name == "removes")
            {
                ReadJsonSet(ref reader, set._removes, serializer);
            }
            else
            {
                reader.Skip();
            }
        }

        return set;
    }

    /// <inheritdoc/>
    public bool Equals(TwoPhaseSet<T>? other) =>
        other is not null && _adds.SetEquals(other._adds) && _removes.SetEquals(other._removes);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as TwoPhaseSet<T>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (T element in _adds)
        {
            hash ^= HashCode.Combine(1, element);
        }

        foreach (T element in _removes)
        {
            hash ^= HashCode.Combine(2, element);
        }

        return hash;
    }

    private TwoPhaseSet<T> Delta() => _delta ??= new TwoPhaseSet<T>(_adds.Comparer);

    private static void WriteSet(ref CrdtWriter writer, HashSet<T> set, ICrdtValueSerializer<T> serializer)
    {
        writer.WriteVarUInt64((ulong)set.Count);
        foreach (T element in set)
        {
            serializer.Write(ref writer, element);
        }
    }

    private static void ReadSet(ref CrdtReader reader, HashSet<T> set, ICrdtValueSerializer<T> serializer)
    {
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            set.Add(serializer.Read(ref reader));
        }
    }

    private static void WriteJsonSet(Utf8JsonWriter writer, HashSet<T> set, ICrdtValueSerializer<T> serializer)
    {
        writer.WriteStartArray();
        foreach (T element in set)
        {
            serializer.WriteJson(writer, element);
        }

        writer.WriteEndArray();
    }

    private static void ReadJsonSet(
        ref Utf8JsonReader reader,
        HashSet<T> set,
        ICrdtValueSerializer<T> serializer)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            set.Add(serializer.ReadJson(ref reader));
        }
    }

    private static CrdtOrder CompareSet(HashSet<T> left, HashSet<T> right)
    {
        bool leftHasExtra = HasElementNotIn(left, right);
        bool rightHasExtra = HasElementNotIn(right, left);
        return (leftHasExtra, rightHasExtra) switch
        {
            (true, true) => CrdtOrder.Concurrent,
            (true, false) => CrdtOrder.Greater,
            (false, true) => CrdtOrder.Less,
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

    private static bool HasElementNotIn(HashSet<T> source, HashSet<T> other)
    {
        foreach (T element in source)
        {
            if (!other.Contains(element))
            {
                return true;
            }
        }

        return false;
    }
}
