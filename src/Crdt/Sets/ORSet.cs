// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>Identifies the kind of change described by a <see cref="ORSetOperation{T}"/>.</summary>
public enum ORSetOperationKind
{
    /// <summary>Inserts a dot and element.</summary>
    Add = 0,

    /// <summary>Removes observed dots.</summary>
    Remove = 1,
}

/// <summary>Describes an idempotent add or observed-remove operation for an <see cref="ORSet{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct ORSetOperation<T>
    where T : notnull
{
    /// <summary>Initializes an add operation.</summary>
    /// <param name="dot">The dot assigned to the added element.</param>
    /// <param name="element">The added element.</param>
    public ORSetOperation(Dot dot, T element)
    {
        Kind = ORSetOperationKind.Add;
        Dot = dot;
        Element = element;
        RemovedDots = [];
    }

    /// <summary>Initializes a remove operation.</summary>
    /// <param name="removedDots">The observed dots removed by the operation.</param>
    public ORSetOperation(IReadOnlyCollection<Dot> removedDots)
    {
        Throw.IfNull(removedDots);
        Kind = ORSetOperationKind.Remove;
        Dot = default;
        Element = default;
        RemovedDots = [.. removedDots];
    }

    /// <summary>Gets the operation kind.</summary>
    public ORSetOperationKind Kind { get; }

    /// <summary>Gets the added dot for add operations.</summary>
    public Dot Dot { get; }

    /// <summary>Gets the added element for add operations.</summary>
    public T? Element { get; }

    /// <summary>Gets the observed dots removed by remove operations.</summary>
    public IReadOnlyCollection<Dot> RemovedDots { get; }

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        if (Kind == ORSetOperationKind.Add)
        {
            writer.WriteDot(Dot);
            serializer.Write(ref writer, Element!);
            return;
        }

        writer.WriteVarUInt64((ulong)RemovedDots.Count);
        foreach (Dot dot in RemovedDots)
        {
            writer.WriteDot(dot);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static ORSetOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var kind = (ORSetOperationKind)reader.ReadByte();
        if (kind == ORSetOperationKind.Add)
        {
            Dot dot = reader.ReadDot();
            return new ORSetOperation<T>(dot, serializer.Read(ref reader));
        }

        int count = reader.ReadCount();
        var dots = new Dot[count];
        for (int i = 0; i < count; i++)
        {
            dots[i] = reader.ReadDot();
        }

        return new ORSetOperation<T>(dots);
    }
}

/// <summary>
/// An observed-remove set (OR-Set/ORSWOT): each add receives a unique dot, and removes only
/// the dots observed at removal time. Concurrent adds therefore win over removes.
/// </summary>
/// <typeparam name="T">The element type; must be non-null and have value equality.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class ORSet<T> :
    IConvergent<ORSet<T>>,
    IDeltaConvergent<ORSet<T>, ORSet<T>>,
    IOperationConvergent<ORSetOperation<T>>,
    IEquatable<ORSet<T>>
    where T : notnull
{
    private readonly DotKernel<T> _kernel;
    private readonly IEqualityComparer<T> _comparer;
    private DotKernel<T>? _delta;

    /// <summary>Initializes an empty observed-remove set using the default equality comparer.</summary>
    public ORSet()
        : this(EqualityComparer<T>.Default)
    {
    }

    /// <summary>Initializes an empty observed-remove set using a custom equality comparer.</summary>
    /// <param name="comparer">The element equality comparer.</param>
    public ORSet(IEqualityComparer<T> comparer)
    {
        _kernel = new DotKernel<T>();
        _comparer = comparer;
    }

    private ORSet(DotKernel<T> kernel, IEqualityComparer<T> comparer)
    {
        _kernel = kernel;
        _comparer = comparer;
    }

    /// <summary>Gets the number of live add dots in the set.</summary>
    public int DotCount => _kernel.Count;

    /// <summary>Gets the number of distinct elements currently present.</summary>
    public int Count
    {
        get
        {
            var elements = new HashSet<T>(_comparer);
            foreach (T value in _kernel.Values)
            {
                elements.Add(value);
            }

            return elements.Count;
        }
    }

    /// <summary>Gets the distinct elements currently present.</summary>
    public IReadOnlyCollection<T> Elements
    {
        get
        {
            var elements = new HashSet<T>(_comparer);
            foreach (T value in _kernel.Values)
            {
                elements.Add(value);
            }

            return elements;
        }
    }

    /// <summary>Determines whether <paramref name="element"/> is currently present.</summary>
    /// <param name="element">The element to test.</param>
    /// <returns><see langword="true"/> if any live dot stores the element.</returns>
    public bool Contains(T element)
    {
        foreach (T value in _kernel.Values)
        {
            if (_comparer.Equals(value, element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds <paramref name="element"/> under a fresh dot from <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica performing the add.</param>
    /// <param name="element">The element to add.</param>
    /// <returns>The add operation to broadcast.</returns>
    public ORSetOperation<T> Add(ReplicaId replica, T element)
    {
        Throw.IfNull(element);
        Dot dot = _kernel.Add(replica, element);
        Delta().Insert(dot, element);
        return new ORSetOperation<T>(dot, element);
    }

    /// <summary>Removes every currently observed dot for <paramref name="element"/>.</summary>
    /// <param name="element">The element to remove.</param>
    /// <returns>The remove operation to broadcast.</returns>
    public ORSetOperation<T> Remove(T element)
    {
        Throw.IfNull(element);
        var removed = new List<Dot>();
        foreach (KeyValuePair<Dot, T> entry in _kernel.Entries)
        {
            if (_comparer.Equals(entry.Value, element))
            {
                removed.Add(entry.Key);
            }
        }

        foreach (Dot dot in removed)
        {
            _kernel.RemoveDot(dot);
            DotKernel<T> delta = Delta();
            delta.RemoveDot(dot);
            delta.Context.Add(dot);
        }

        return new ORSetOperation<T>(removed);
    }

    /// <inheritdoc/>
    public void Merge(ORSet<T> other)
    {
        Throw.IfNull(other);
        _kernel.Merge(other._kernel);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(ORSet<T> other)
    {
        Throw.IfNull(other);
        if (Equals(other))
        {
            return CrdtOrder.Equal;
        }

        ORSet<T> left = Clone();
        left.Merge(other);
        if (left.Equals(other))
        {
            return CrdtOrder.Less;
        }

        ORSet<T> right = other.Clone();
        right.Merge(this);
        return right.Equals(this) ? CrdtOrder.Greater : CrdtOrder.Concurrent;
    }

    /// <inheritdoc/>
    public ORSet<T> Clone() => new(_kernel.Clone(), _comparer);

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out ORSet<T> delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = new ORSet<T>(_delta, _comparer);
        _delta = null;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(ORSet<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(ORSetOperation<T> operation)
    {
        if (operation.Kind == ORSetOperationKind.Add)
        {
            if (!_kernel.Entries.ContainsKey(operation.Dot) && _kernel.Context.Contains(operation.Dot))
            {
                return false;
            }

            bool changed = !_kernel.Entries.ContainsKey(operation.Dot);
            _kernel.Insert(operation.Dot, operation.Element!);
            return changed;
        }

        bool removed = false;
        foreach (Dot dot in operation.RemovedDots)
        {
            removed |= _kernel.RemoveDot(dot);
            _kernel.Context.Add(dot);
        }

        return removed;
    }

    /// <summary>Serializes the set to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteVarUInt64((ulong)_kernel.Count);
        foreach (KeyValuePair<Dot, T> entry in _kernel.SortedEntries())
        {
            writer.WriteDot(entry.Key);
            serializer.Write(ref writer, entry.Value);
        }

        _kernel.Context.Write(ref writer);
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
    public static ORSet<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var set = new ORSet<T>();
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            Dot dot = reader.ReadDot();
            set._kernel.Insert(dot, serializer.Read(ref reader));
        }

        DotContext context = DotContext.Read(ref reader);
        set._kernel.Context.Merge(context);
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
            writer.WritePropertyName("entries");
            WriteJsonEntries(writer, serializer);
            writer.WritePropertyName("context");
            WriteJsonContext(writer, _kernel.Context);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a set from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded set.</returns>
    public static ORSet<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        var set = new ORSet<T>();
        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? name = reader.GetString();
            reader.Read();
            if (name == "entries")
            {
                ReadJsonEntries(ref reader, set._kernel, serializer);
            }
            else if (name == "context")
            {
                ReadJsonContext(ref reader, set._kernel.Context);
            }
            else
            {
                reader.Skip();
            }
        }

        return set;
    }

    /// <inheritdoc/>
    public bool Equals(ORSet<T>? other)
    {
        if (other is null || _kernel.Count != other._kernel.Count)
        {
            return false;
        }

        foreach (KeyValuePair<Dot, T> entry in _kernel.Entries)
        {
            if (!other._kernel.Entries.TryGetValue(entry.Key, out T? value) || !_comparer.Equals(entry.Value, value))
            {
                return false;
            }
        }

        return ContextEquals(_kernel.Context, other._kernel.Context);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ORSet<T>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (KeyValuePair<Dot, T> entry in _kernel.Entries)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        foreach (KeyValuePair<ReplicaId, ulong> entry in _kernel.Context.CompactEntries())
        {
            hash ^= HashCode.Combine(1, entry.Key, entry.Value);
        }

        foreach (Dot dot in _kernel.Context.CloudDots())
        {
            hash ^= HashCode.Combine(2, dot);
        }

        return hash;
    }

    private DotKernel<T> Delta() => _delta ??= new DotKernel<T>();

    private void WriteJsonEntries(Utf8JsonWriter writer, ICrdtValueSerializer<T> serializer)
    {
        writer.WriteStartArray();
        foreach (KeyValuePair<Dot, T> entry in _kernel.SortedEntries())
        {
            writer.WriteStartObject();
            writer.WritePropertyName("dot");
            WriteJsonDot(writer, entry.Key);
            writer.WritePropertyName("element");
            serializer.WriteJson(writer, entry.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void ReadJsonEntries(
        ref Utf8JsonReader reader,
        DotKernel<T> kernel,
        ICrdtValueSerializer<T> serializer)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            Dot dot = default;
            T? element = default;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                string? name = reader.GetString();
                reader.Read();
                if (name == "dot")
                {
                    dot = ReadJsonDot(ref reader);
                }
                else if (name == "element")
                {
                    element = serializer.ReadJson(ref reader);
                }
                else
                {
                    reader.Skip();
                }
            }

            kernel.Insert(dot, element!);
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

    private static bool ContextEquals(DotContext left, DotContext right)
    {
        return CompactEquals(left.CompactEntries(), right.CompactEntries()) &&
            DotSequenceEquals(left.CloudDots(), right.CloudDots());
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
}
