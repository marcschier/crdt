// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A resettable positive-negative counter: every increment or decrement is stored under a
/// unique dot, and resets remove exactly the contributions observed at reset time.
/// Concurrent contributions therefore survive the reset and remain part of <see cref="Value"/>.
/// </summary>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class ResettableCounter :
    IConvergent<ResettableCounter>,
    IOperationConvergent<ResettableCounterOperation>,
    IBinaryWritable,
    IEquatable<ResettableCounter>
{
    private readonly DotContext _context;
    private readonly Dictionary<Dot, long> _increments;

    /// <summary>Initializes an empty resettable positive-negative counter.</summary>
    public ResettableCounter()
    {
        _context = new DotContext();
        _increments = [];
    }

    private ResettableCounter(DotContext context, Dictionary<Dot, long> increments)
    {
        _context = context;
        _increments = increments;
    }

    /// <summary>Gets the counter's value: the sum of live increment and decrement contributions.</summary>
    public long Value
    {
        get
        {
            long sum = 0;
            foreach (long amount in _increments.Values)
            {
                sum += amount;
            }

            return sum;
        }
    }

    /// <summary>Adds a positive contribution on behalf of <paramref name="replica"/>.</summary>
    /// <param name="replica">The local replica performing the increment.</param>
    /// <param name="amount">The positive amount to add.</param>
    /// <returns>The operation to broadcast.</returns>
    public ResettableCounterOperation Increment(ReplicaId replica, long amount = 1)
    {
        if (amount <= 0)
        {
            Throw.ArgumentOutOfRange(nameof(amount), "Increment amount must be positive.");
        }

        return Add(replica, amount);
    }

    /// <summary>Adds a negative contribution on behalf of <paramref name="replica"/>.</summary>
    /// <param name="replica">The local replica performing the decrement.</param>
    /// <param name="amount">The positive amount to subtract.</param>
    /// <returns>The operation to broadcast.</returns>
    public ResettableCounterOperation Decrement(ReplicaId replica, long amount = 1)
    {
        if (amount <= 0)
        {
            Throw.ArgumentOutOfRange(nameof(amount), "Decrement amount must be positive.");
        }

        return Add(replica, -amount);
    }

    /// <summary>Removes every currently observed contribution while preserving causal history.</summary>
    /// <returns>The reset operation to broadcast.</returns>
    public ResettableCounterOperation Reset()
    {
        _increments.Clear();
        return new ResettableCounterOperation(_context);
    }

    /// <inheritdoc/>
    public void Merge(ResettableCounter other)
    {
        Throw.IfNull(other);

        var toRemove = new List<Dot>();
        foreach (KeyValuePair<Dot, long> entry in _increments)
        {
            if (!other._increments.ContainsKey(entry.Key) && other._context.Contains(entry.Key))
            {
                toRemove.Add(entry.Key);
            }
        }

        foreach (Dot dot in toRemove)
        {
            _increments.Remove(dot);
        }

        foreach (KeyValuePair<Dot, long> entry in other._increments)
        {
            if (!_increments.ContainsKey(entry.Key) && !_context.Contains(entry.Key))
            {
                _increments[entry.Key] = entry.Value;
            }
        }

        _context.Merge(other._context);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(ResettableCounter other)
    {
        Throw.IfNull(other);
        if (Equals(other))
        {
            return CrdtOrder.Equal;
        }

        ResettableCounter left = Clone();
        left.Merge(other);
        if (left.Equals(other))
        {
            return CrdtOrder.Less;
        }

        ResettableCounter right = other.Clone();
        right.Merge(this);
        return right.Equals(this) ? CrdtOrder.Greater : CrdtOrder.Concurrent;
    }

    /// <inheritdoc/>
    public ResettableCounter Clone() => new(_context.Clone(), new Dictionary<Dot, long>(_increments));

    /// <inheritdoc/>
    public bool Apply(ResettableCounterOperation operation)
    {
        if (operation.Kind == ResettableCounterOperationKind.Increment)
        {
            if (_context.Contains(operation.Dot))
            {
                return false;
            }

            _context.Add(operation.Dot);
            _increments[operation.Dot] = operation.Amount;
            return true;
        }

        bool changed = false;
        foreach (Dot dot in new List<Dot>(_increments.Keys))
        {
            if (operation.Context.Contains(dot))
            {
                changed |= _increments.Remove(dot);
            }
        }

        if (!ContextEquals(_context, operation.Context))
        {
            DotContext merged = _context.Clone();
            merged.Merge(operation.Context);
            changed |= !ContextEquals(_context, merged);
            _context.Merge(operation.Context);
        }

        return changed;
    }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)_increments.Count);
        foreach (KeyValuePair<Dot, long> entry in SortedIncrements())
        {
            writer.WriteDot(entry.Key);
            writer.WriteVarInt64(entry.Value);
        }

        _context.Write(ref writer);
    }

    /// <summary>Decodes a <see cref="ResettableCounter"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded counter.</returns>
    public static ResettableCounter ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static ResettableCounter Read(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var increments = new Dictionary<Dot, long>();
        for (int i = 0; i < count; i++)
        {
            increments[reader.ReadDot()] = reader.ReadVarInt64();
        }

        DotContext context = DotContext.Read(ref reader);
        foreach (Dot dot in increments.Keys)
        {
            context.Add(dot);
        }

        return new ResettableCounter(context, increments);
    }

    /// <summary>Serializes this counter to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(ToDto(), CrdtResettableCounterJson.Default.ResettableCounterDto);
    }

    /// <summary>Deserializes a <see cref="ResettableCounter"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded counter.</returns>
    public static ResettableCounter FromJson(string json)
    {
        Throw.IfNull(json);
        ResettableCounterDto? dto =
            JsonSerializer.Deserialize(json, CrdtResettableCounterJson.Default.ResettableCounterDto);
        return dto is null ? new ResettableCounter() : FromDto(dto);
    }

    /// <inheritdoc/>
    public bool Equals(ResettableCounter? other)
    {
        if (other is null || _increments.Count != other._increments.Count)
        {
            return false;
        }

        foreach (KeyValuePair<Dot, long> entry in _increments)
        {
            if (!other._increments.TryGetValue(entry.Key, out long value) || value != entry.Value)
            {
                return false;
            }
        }

        return ContextEquals(_context, other._context);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ResettableCounter);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (KeyValuePair<Dot, long> entry in _increments)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value);
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

    internal static bool ContextEquals(DotContext left, DotContext right)
    {
        return CompactEquals(left.CompactEntries(), right.CompactEntries()) &&
            DotSequenceEquals(left.CloudDots(), right.CloudDots());
    }

    internal ResettableCounterDto ToDto()
    {
        var entries = new List<ResettableCounterIncrementDto>();
        foreach (KeyValuePair<Dot, long> entry in SortedIncrements())
        {
            entries.Add(new ResettableCounterIncrementDto(ToDto(entry.Key), entry.Value));
        }

        return new ResettableCounterDto([.. entries], ToDto(_context));
    }

    internal static ResettableCounter FromDto(ResettableCounterDto dto)
    {
        var increments = new Dictionary<Dot, long>();
        foreach (ResettableCounterIncrementDto entry in dto.Increments)
        {
            increments[FromDto(entry.Dot)] = entry.Amount;
        }

        DotContext context = FromDto(dto.Context);
        foreach (Dot dot in increments.Keys)
        {
            context.Add(dot);
        }

        return new ResettableCounter(context, increments);
    }

    private ResettableCounterOperation Add(ReplicaId replica, long amount)
    {
        Dot dot = _context.NextDot(replica);
        _increments[dot] = amount;
        return new ResettableCounterOperation(dot, amount);
    }

    private List<KeyValuePair<Dot, long>> SortedIncrements()
    {
        var list = new List<KeyValuePair<Dot, long>>(_increments);
        list.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return list;
    }

    private static ResettableCounterContextDto ToDto(DotContext context)
    {
        var compact = new List<ResettableCounterCompactDto>();
        foreach (KeyValuePair<ReplicaId, ulong> entry in context.CompactEntries())
        {
            compact.Add(new ResettableCounterCompactDto(entry.Key, entry.Value));
        }

        var cloud = new List<ResettableCounterDotDto>();
        foreach (Dot dot in context.CloudDots())
        {
            cloud.Add(ToDto(dot));
        }

        return new ResettableCounterContextDto([.. compact], [.. cloud]);
    }

    private static DotContext FromDto(ResettableCounterContextDto dto)
    {
        var context = new DotContext();
        foreach (ResettableCounterCompactDto entry in dto.Compact)
        {
            for (ulong sequence = 1; sequence <= entry.Sequence; sequence++)
            {
                context.Add(new Dot(entry.Replica, sequence));
            }
        }

        foreach (ResettableCounterDotDto dot in dto.Cloud)
        {
            context.Add(FromDto(dot));
        }

        return context;
    }

    private static ResettableCounterDotDto ToDto(Dot dot) =>
        new(dot.Replica, dot.Sequence);

    private static Dot FromDto(ResettableCounterDotDto dto) =>
        new(dto.Replica, dto.Sequence);

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
