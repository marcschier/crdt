// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A grow-only set (G-Set): a state-based, delta-state, and operation-based CRDT supporting
/// only additions. Merging two G-Sets yields their union, which is commutative, associative,
/// and idempotent.
/// </summary>
/// <typeparam name="T">The element type; must be non-null and have value equality.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class GSet<T> :
    IConvergent<GSet<T>>,
    IDeltaConvergent<GSet<T>, GSet<T>>,
    IOperationConvergent<GSetOperation<T>>,
    IEquatable<GSet<T>>
    where T : notnull
{
    private readonly HashSet<T> _elements;
    private HashSet<T>? _delta;

    /// <summary>Initializes an empty grow-only set using the default equality comparer.</summary>
    public GSet() => _elements = [];

    /// <summary>Initializes an empty grow-only set using a custom equality comparer.</summary>
    /// <param name="comparer">The element equality comparer.</param>
    public GSet(IEqualityComparer<T> comparer) => _elements = new HashSet<T>(comparer);

    private GSet(HashSet<T> elements) => _elements = elements;

    /// <summary>Gets the number of elements in the set.</summary>
    public int Count => _elements.Count;

    /// <summary>Gets the set's elements.</summary>
    public IReadOnlyCollection<T> Elements => _elements;

    /// <summary>Determines whether <paramref name="element"/> is present.</summary>
    /// <param name="element">The element to test.</param>
    /// <returns><see langword="true"/> if present.</returns>
    public bool Contains(T element) => _elements.Contains(element);

    /// <summary>Adds <paramref name="element"/> and returns the operation to broadcast.</summary>
    /// <param name="element">The element to add.</param>
    /// <returns>The add operation.</returns>
    public GSetOperation<T> Add(T element)
    {
        Throw.IfNull(element);
        if (_elements.Add(element))
        {
            (_delta ??= new HashSet<T>(_elements.Comparer)).Add(element);
        }

        return new GSetOperation<T>(element);
    }

    /// <inheritdoc/>
    public void Merge(GSet<T> other)
    {
        Throw.IfNull(other);
        foreach (T element in other._elements)
        {
            _elements.Add(element);
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(GSet<T> other)
    {
        Throw.IfNull(other);
        bool thisHasExtra = HasElementNotIn(_elements, other._elements);
        bool otherHasExtra = HasElementNotIn(other._elements, _elements);

        return (thisHasExtra, otherHasExtra) switch
        {
            (true, true) => CrdtOrder.Concurrent,
            (true, false) => CrdtOrder.Greater,
            (false, true) => CrdtOrder.Less,
            _ => CrdtOrder.Equal,
        };
    }

    /// <inheritdoc/>
    public GSet<T> Clone() => new(new HashSet<T>(_elements, _elements.Comparer));

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out GSet<T> delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = new GSet<T>(_delta);
        _delta = null;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(GSet<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(GSetOperation<T> operation) => _elements.Add(operation.Element);

    /// <summary>Serializes the set to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        Write(ref writer, serializer);
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

    internal void Write(ref CrdtWriter writer, ICrdtValueSerializer<T> serializer)
    {
        writer.WriteVarUInt64((ulong)_elements.Count);
        foreach (T element in _elements)
        {
            serializer.Write(ref writer, element);
        }
    }

    /// <summary>Decodes a set from the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded set.</returns>
    public static GSet<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return Read(ref reader, serializer);
    }

    internal static GSet<T> Read(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        int count = reader.ReadCount();
        var set = new GSet<T>();
        for (int i = 0; i < count; i++)
        {
            set._elements.Add(serializer.Read(ref reader));
        }

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
            writer.WriteStartArray();
            foreach (T element in _elements)
            {
                serializer.WriteJson(writer, element);
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a set from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded set.</returns>
    public static GSet<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var set = new GSet<T>();
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            set._elements.Add(serializer.ReadJson(ref reader));
        }

        return set;
    }

    /// <inheritdoc/>
    public bool Equals(GSet<T>? other) => other is not null && _elements.SetEquals(other._elements);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as GSet<T>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (T element in _elements)
        {
            hash ^= element.GetHashCode();
        }

        return hash;
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
