// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A positive-negative counter (PN-Counter): a state-based, delta-state, and operation-based
/// CRDT supporting both increments and decrements. It composes two grow-only counters — one
/// for increments, one for decrements — and reports their difference as its
/// <see cref="Value"/>.
/// </summary>
/// <remarks>
/// The internal state grows monotonically even though the observable value may decrease.
/// Individual operations are thread-safe and observe a coherent positive/negative pair.
/// Compound sequences of calls still require external synchronization.
/// </remarks>
public sealed class PNCounter :
    IConvergent<PNCounter>,
    IDeltaConvergent<PNCounter, PNCounter>,
    IOperationConvergent<PNCounterOperation>,
    IBinaryWritable,
    IEquatable<PNCounter>
{
    private static long _nextLockOrder;
    private readonly object _gate = new();
    private readonly long _lockOrder = Interlocked.Increment(ref _nextLockOrder);
    // Children are exclusively owned; acquire PN gates before their GCounter gates.
    private readonly GCounter _positive;
    private readonly GCounter _negative;

    /// <summary>Initializes an empty positive-negative counter.</summary>
    public PNCounter()
    {
        _positive = new GCounter();
        _negative = new GCounter();
    }

    private PNCounter(GCounter positive, GCounter negative)
    {
        _positive = positive;
        _negative = negative;
    }

    /// <summary>Gets the counter's value: total increments minus total decrements.</summary>
    public long Value
    {
        get
        {
            lock (_gate)
            {
                return unchecked((long)_positive.Value - (long)_negative.Value);
            }
        }
    }

    /// <summary>Increments the counter on behalf of <paramref name="replica"/>.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="amount">The amount to add (must be positive).</param>
    /// <returns>The operation to broadcast.</returns>
    public PNCounterOperation Increment(ReplicaId replica, ulong amount = 1)
    {
        lock (_gate)
        {
            _positive.Increment(replica, amount);
            return new PNCounterOperation(replica, _positive[replica], _negative[replica]);
        }
    }

    /// <summary>Decrements the counter on behalf of <paramref name="replica"/>.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="amount">The amount to subtract (must be positive).</param>
    /// <returns>The operation to broadcast.</returns>
    public PNCounterOperation Decrement(ReplicaId replica, ulong amount = 1)
    {
        lock (_gate)
        {
            _negative.Increment(replica, amount);
            return new PNCounterOperation(replica, _positive[replica], _negative[replica]);
        }
    }

    /// <inheritdoc/>
    public void Merge(PNCounter other)
    {
        Throw.IfNull(other);
        PNCounter first = _lockOrder < other._lockOrder ? this : other;
        PNCounter second = ReferenceEquals(first, this) ? other : this;
        lock (first._gate)
        {
            lock (second._gate)
            {
                _positive.Merge(other._positive);
                _negative.Merge(other._negative);
            }
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(PNCounter other)
    {
        Throw.IfNull(other);
        PNCounter first = _lockOrder < other._lockOrder ? this : other;
        PNCounter second = ReferenceEquals(first, this) ? other : this;
        lock (first._gate)
        {
            lock (second._gate)
            {
                return CombineOrders(_positive.Compare(other._positive), _negative.Compare(other._negative));
            }
        }
    }

    /// <inheritdoc/>
    public PNCounter Clone()
    {
        lock (_gate)
        {
            return new PNCounter(_positive.Clone(), _negative.Clone());
        }
    }

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out PNCounter delta)
    {
        lock (_gate)
        {
            bool hasPositive = _positive.TryExtractDelta(out GCounter? positiveDelta);
            bool hasNegative = _negative.TryExtractDelta(out GCounter? negativeDelta);
            if (!hasPositive && !hasNegative)
            {
                delta = null;
                return false;
            }

            delta = new PNCounter(positiveDelta ?? new GCounter(), negativeDelta ?? new GCounter());
            return true;
        }
    }

    /// <inheritdoc/>
    public void MergeDelta(PNCounter delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(PNCounterOperation operation)
    {
        lock (_gate)
        {
            bool changedPositive = _positive.Apply(new GCounterOperation(operation.Replica, operation.Positive));
            bool changedNegative = _negative.Apply(new GCounterOperation(operation.Replica, operation.Negative));
            return changedPositive || changedNegative;
        }
    }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        List<KeyValuePair<ReplicaId, ulong>> positive;
        List<KeyValuePair<ReplicaId, ulong>> negative;
        lock (_gate)
        {
            positive = _positive.SortedCounts();
            negative = _negative.SortedCounts();
        }

        // User-provided buffer writers run only after the counter gates are released.
        GCounter.WriteCounts(ref writer, positive);
        GCounter.WriteCounts(ref writer, negative);
    }

    /// <summary>Decodes a <see cref="PNCounter"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded counter.</returns>
    public static PNCounter ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        GCounter positive = GCounter.Read(ref reader);
        GCounter negative = GCounter.Read(ref reader);
        return new PNCounter(positive, negative);
    }

    /// <summary>Serializes this counter to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        PNCounterDto dto;
        lock (_gate)
        {
            dto = new PNCounterDto(ToEntries(_positive), ToEntries(_negative));
        }

        return JsonSerializer.Serialize(dto, CrdtJson.Default.PNCounterDto);
    }

    /// <summary>Deserializes a <see cref="PNCounter"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded counter.</returns>
    public static PNCounter FromJson(string json)
    {
        Throw.IfNull(json);
        PNCounterDto? dto = JsonSerializer.Deserialize(json, CrdtJson.Default.PNCounterDto);
        if (dto is null)
        {
            return new PNCounter();
        }

        return new PNCounter(FromEntries(dto.P), FromEntries(dto.N));
    }

    /// <inheritdoc/>
    public bool Equals(PNCounter? other)
    {
        if (other is null)
        {
            return false;
        }

        PNCounter first = _lockOrder < other._lockOrder ? this : other;
        PNCounter second = ReferenceEquals(first, this) ? other : this;
        lock (first._gate)
        {
            lock (second._gate)
            {
                return _positive.Equals(other._positive) && _negative.Equals(other._negative);
            }
        }
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PNCounter);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        lock (_gate)
        {
            return HashCode.Combine(_positive, _negative);
        }
    }

    private static CounterEntryDto[] ToEntries(GCounter counter)
    {
        var entries = new List<CounterEntryDto>();
        foreach (KeyValuePair<ReplicaId, ulong> entry in counter.SortedCounts())
        {
            entries.Add(new CounterEntryDto(entry.Key, entry.Value));
        }

        return [.. entries];
    }

    private static GCounter FromEntries(CounterEntryDto[] entries)
    {
        var counter = new GCounter();
        foreach (CounterEntryDto entry in entries)
        {
            counter.SetCount(entry.Replica, entry.Value);
        }

        return counter;
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
}
