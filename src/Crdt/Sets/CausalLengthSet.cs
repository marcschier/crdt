// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// A causal-length set: each element carries a monotonically increasing length, where odd
/// lengths mean present and even lengths mean absent. Merging keeps the maximum length per
/// element, so later add/remove cycles dominate earlier observations without tombstone sets.
/// </summary>
/// <typeparam name="T">The element type; must be non-null and have value equality.</typeparam>
/// <remarks>
/// Mutable and not thread-safe. Lengths are per-element counters rather than replica-stamped
/// dots, so a <see cref="StableCut"/> alone cannot identify which removed elements are
/// all-observed. Coordinators that prove removed element keys are observed everywhere may use
/// the internal all-observed collection hook to remove those absent entries.
/// </remarks>
public sealed partial class CausalLengthSet<T> :
    IConvergent<CausalLengthSet<T>>,
    IDeltaConvergent<CausalLengthSet<T>, CausalLengthSet<T>>,
    IOperationConvergent<CausalLengthSetOperation<T>>,
    IGarbageCollectable,
    IEquatable<CausalLengthSet<T>>
    where T : notnull
{
    private readonly Dictionary<T, ulong> _lengths;
    private Dictionary<T, ulong>? _delta;

    /// <summary>Initializes an empty causal-length set using the default equality comparer.</summary>
    public CausalLengthSet()
        : this(EqualityComparer<T>.Default)
    {
    }

    /// <summary>Initializes an empty causal-length set using a custom equality comparer.</summary>
    /// <param name="comparer">The element equality comparer.</param>
    public CausalLengthSet(IEqualityComparer<T> comparer) => _lengths = new Dictionary<T, ulong>(comparer);

    private CausalLengthSet(Dictionary<T, ulong> lengths) => _lengths = lengths;

    /// <summary>Gets the number of elements currently present.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (ulong length in _lengths.Values)
            {
                if (IsPresent(length))
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
            foreach (KeyValuePair<T, ulong> entry in _lengths)
            {
                if (IsPresent(entry.Value))
                {
                    elements.Add(entry.Key);
                }
            }

            return elements;
        }
    }

    /// <inheritdoc/>
    public VersionVector ObservedVersion => new();

    /// <summary>Determines whether <paramref name="element"/> is currently present.</summary>
    /// <param name="element">The element to test.</param>
    /// <returns><see langword="true"/> if the element has an odd length.</returns>
    public bool Contains(T element) => _lengths.TryGetValue(element, out ulong length) && IsPresent(length);

    /// <summary>Adds <paramref name="element"/> when it is currently absent.</summary>
    /// <param name="element">The element to add.</param>
    /// <returns>The operation to broadcast, including the resulting causal length.</returns>
    public CausalLengthSetOperation<T> Add(T element)
    {
        Throw.IfNull(element);
        ulong current = CurrentLength(element);
        if (!IsPresent(current))
        {
            current++;
            SetLength(element, current, recordDelta: true);
        }

        return new CausalLengthSetOperation<T>(element, current);
    }

    /// <summary>Removes <paramref name="element"/> when it is currently present.</summary>
    /// <param name="element">The element to remove.</param>
    /// <returns>The operation to broadcast, including the resulting causal length.</returns>
    public CausalLengthSetOperation<T> Remove(T element)
    {
        Throw.IfNull(element);
        ulong current = CurrentLength(element);
        if (IsPresent(current))
        {
            current++;
            SetLength(element, current, recordDelta: true);
        }

        return new CausalLengthSetOperation<T>(element, current);
    }

    /// <inheritdoc/>
    public void Merge(CausalLengthSet<T> other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<T, ulong> entry in other._lengths)
        {
            if (entry.Value > CurrentLength(entry.Key))
            {
                SetLength(entry.Key, entry.Value, recordDelta: false);
            }
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(CausalLengthSet<T> other)
    {
        Throw.IfNull(other);
        bool thisHasGreater = HasGreaterLengthThan(_lengths, other._lengths);
        bool otherHasGreater = HasGreaterLengthThan(other._lengths, _lengths);

        return (thisHasGreater, otherHasGreater) switch
        {
            (true, true) => CrdtOrder.Concurrent,
            (true, false) => CrdtOrder.Greater,
            (false, true) => CrdtOrder.Less,
            _ => CrdtOrder.Equal,
        };
    }

    /// <inheritdoc/>
    public CausalLengthSet<T> Clone() => new(new Dictionary<T, ulong>(_lengths, _lengths.Comparer));

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out CausalLengthSet<T> delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = new CausalLengthSet<T>(new Dictionary<T, ulong>(_delta, _lengths.Comparer));
        _delta = null;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(CausalLengthSet<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(CausalLengthSetOperation<T> operation)
    {
        if (operation.Length > CurrentLength(operation.Element))
        {
            SetLength(operation.Element, operation.Length, recordDelta: false);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void CollectStable(StableCut cut)
    {
        Throw.IfNull(cut);
    }

    internal void CollectAllObserved(IEnumerable<T> stableRemovedElements)
    {
        Throw.IfNull(stableRemovedElements);
        foreach (T element in stableRemovedElements)
        {
            if (_lengths.TryGetValue(element, out ulong length) && !IsPresent(length))
            {
                _lengths.Remove(element);
            }
        }
    }

    /// <summary>Serializes the set to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        List<SerializedEntry> entries = SortedEntries(serializer);
        writer.WriteVarUInt64((ulong)entries.Count);
        foreach (SerializedEntry entry in entries)
        {
            writer.WriteRaw(entry.ElementBytes);
            writer.WriteVarUInt64(entry.Length);
        }
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
    public static CausalLengthSet<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var set = new CausalLengthSet<T>();
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            T element = serializer.Read(ref reader);
            ulong length = reader.ReadVarUInt64();
            if (length != 0UL)
            {
                set._lengths[element] = length;
            }
        }

        return set;
    }

    /// <inheritdoc/>
    public bool Equals(CausalLengthSet<T>? other)
    {
        if (other is null || _lengths.Count != other._lengths.Count)
        {
            return false;
        }

        foreach (KeyValuePair<T, ulong> entry in _lengths)
        {
            if (!other._lengths.TryGetValue(entry.Key, out ulong length) || entry.Value != length)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as CausalLengthSet<T>);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (KeyValuePair<T, ulong> entry in _lengths)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return hash;
    }

    private static bool IsPresent(ulong length) => (length & 1UL) == 1UL;

    private ulong CurrentLength(T element) => _lengths.TryGetValue(element, out ulong length) ? length : 0UL;

    private void SetLength(T element, ulong length, bool recordDelta)
    {
        if (length == 0UL)
        {
            return;
        }

        _lengths[element] = length;
        if (recordDelta)
        {
            (_delta ??= new Dictionary<T, ulong>(_lengths.Comparer))[element] = length;
        }
    }

    private List<SerializedEntry> SortedEntries(ICrdtValueSerializer<T> serializer)
    {
        var entries = new List<SerializedEntry>(_lengths.Count);
        foreach (KeyValuePair<T, ulong> entry in _lengths)
        {
            using var buffer = new PooledBufferWriter();
            var writer = new CrdtWriter(buffer);
            serializer.Write(ref writer, entry.Key);
            entries.Add(new SerializedEntry(entry.Key, entry.Value, buffer.WrittenSpan.ToArray()));
        }

        entries.Sort(static (left, right) => CompareBytes(left.ElementBytes, right.ElementBytes));
        return entries;
    }

    private static int CompareBytes(byte[] left, byte[] right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int i = 0; i < length; i++)
        {
            int comparison = left[i].CompareTo(right[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    private static bool HasGreaterLengthThan(Dictionary<T, ulong> left, Dictionary<T, ulong> right)
    {
        foreach (KeyValuePair<T, ulong> entry in left)
        {
            if (!right.TryGetValue(entry.Key, out ulong length) || entry.Value > length)
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct SerializedEntry
    {
        public SerializedEntry(T element, ulong length, byte[] elementBytes)
        {
            Element = element;
            Length = length;
            ElementBytes = elementBytes;
        }

        public T Element { get; }

        public ulong Length { get; }

        public byte[] ElementBytes { get; }
    }
}
