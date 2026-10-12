// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A grow-only counter (G-Counter): a state-based, delta-state, and operation-based CRDT
/// that supports only increments. Each replica owns a slot; the counter's
/// <see cref="Value"/> is the sum of all slots and merging takes the per-replica maximum.
/// </summary>
/// <remarks>
/// <para>
/// The same instance supports all three replication models: exchange full state with
/// <see cref="Merge"/>, propagate just recent changes with <see cref="TryExtractDelta"/> /
/// <see cref="MergeDelta"/>, or broadcast the operation returned by <see cref="Increment"/>
/// and replay it with <see cref="Apply"/>. Individual operations are thread-safe;
/// compound sequences of calls still require external synchronization.
/// </para>
/// </remarks>
public sealed class GCounter :
    IConvergent<GCounter>,
    IDeltaConvergent<GCounter, GCounter>,
    IOperationConvergent<GCounterOperation>,
    IBinaryWritable,
    IEquatable<GCounter>
{
    private static long _nextLockOrder;
    private readonly object _gate = new();
    private readonly long _lockOrder = Interlocked.Increment(ref _nextLockOrder);
    private readonly Dictionary<ReplicaId, ulong> _counts;
    private Dictionary<ReplicaId, ulong>? _delta;

    /// <summary>Initializes an empty grow-only counter.</summary>
    public GCounter() => _counts = [];

    private GCounter(Dictionary<ReplicaId, ulong> counts) => _counts = counts;

    /// <summary>Gets the counter's value: the sum of every replica's slot.</summary>
    public ulong Value
    {
        get
        {
            lock (_gate)
            {
                ulong sum = 0;
                foreach (ulong slot in _counts.Values)
                {
                    sum += slot;
                }

                return sum;
            }
        }
    }

    /// <summary>Gets the accumulated total contributed by <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica to look up.</param>
    public ulong this[ReplicaId replica]
    {
        get
        {
            lock (_gate)
            {
                return _counts.TryGetValue(replica, out ulong value) ? value : 0UL;
            }
        }
    }

    /// <summary>
    /// Increments <paramref name="replica"/>'s slot by <paramref name="amount"/> and returns the
    /// operation to broadcast for operation-based replication. The change is also buffered for
    /// delta-state extraction.
    /// </summary>
    /// <param name="replica">The local replica performing the increment.</param>
    /// <param name="amount">The amount to add (must be positive).</param>
    /// <returns>The operation describing the new absolute slot value.</returns>
    public GCounterOperation Increment(ReplicaId replica, ulong amount = 1)
    {
        if (amount == 0)
        {
            Throw.ArgumentOutOfRange(nameof(amount), "Increment amount must be positive.");
        }

        lock (_gate)
        {
            ulong next = this[replica] + amount;
            _counts[replica] = next;
            BufferDelta(replica, next);
            return new GCounterOperation(replica, next);
        }
    }

    /// <inheritdoc/>
    public void Merge(GCounter other)
    {
        Throw.IfNull(other);
        GCounter first = _lockOrder < other._lockOrder ? this : other;
        GCounter second = ReferenceEquals(first, this) ? other : this;
        lock (first._gate)
        {
            lock (second._gate)
            {
                foreach (KeyValuePair<ReplicaId, ulong> entry in other._counts)
                {
                    if (!_counts.TryGetValue(entry.Key, out ulong current) || entry.Value > current)
                    {
                        _counts[entry.Key] = entry.Value;
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(GCounter other)
    {
        Throw.IfNull(other);
        GCounter first = _lockOrder < other._lockOrder ? this : other;
        GCounter second = ReferenceEquals(first, this) ? other : this;
        lock (first._gate)
        {
            lock (second._gate)
            {
                bool less = false;
                bool greater = false;

                foreach (KeyValuePair<ReplicaId, ulong> entry in _counts)
                {
                    ulong otherValue = other[entry.Key];
                    if (entry.Value > otherValue)
                    {
                        greater = true;
                    }
                    else if (entry.Value < otherValue)
                    {
                        less = true;
                    }
                }

                foreach (KeyValuePair<ReplicaId, ulong> entry in other._counts)
                {
                    if (!_counts.ContainsKey(entry.Key) && entry.Value > 0UL)
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
        }
    }

    /// <inheritdoc/>
    public GCounter Clone()
    {
        lock (_gate)
        {
            return new GCounter(new Dictionary<ReplicaId, ulong>(_counts));
        }
    }

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out GCounter delta)
    {
        lock (_gate)
        {
            if (_delta is null)
            {
                delta = null;
                return false;
            }

            delta = new GCounter(_delta);
            _delta = null;
            return true;
        }
    }

    /// <inheritdoc/>
    public void MergeDelta(GCounter delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(GCounterOperation operation)
    {
        lock (_gate)
        {
            ulong current = this[operation.Replica];
            if (operation.Value > current)
            {
                _counts[operation.Replica] = operation.Value;
                return true;
            }

            return false;
        }
    }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer) => WriteCounts(ref writer, SortedCounts());

    /// <summary>Decodes a <see cref="GCounter"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded counter.</returns>
    public static GCounter ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static GCounter Read(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var counter = new GCounter();
        for (int i = 0; i < count; i++)
        {
            ReplicaId replica = reader.ReadReplicaId();
            counter._counts[replica] = reader.ReadVarUInt64();
        }

        return counter;
    }

    /// <summary>Serializes this counter to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        CounterEntryDto[] entries;
        lock (_gate)
        {
            entries = new CounterEntryDto[_counts.Count];
            int i = 0;
            foreach (KeyValuePair<ReplicaId, ulong> entry in SortedCounts())
            {
                entries[i++] = new CounterEntryDto(entry.Key, entry.Value);
            }
        }

        return JsonSerializer.Serialize(entries, CrdtJson.Default.CounterEntryDtoArray);
    }

    /// <summary>Deserializes a <see cref="GCounter"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded counter.</returns>
    public static GCounter FromJson(string json)
    {
        Throw.IfNull(json);
        CounterEntryDto[] entries =
            JsonSerializer.Deserialize(json, CrdtJson.Default.CounterEntryDtoArray) ?? [];
        var counter = new GCounter();
        foreach (CounterEntryDto entry in entries)
        {
            counter._counts[entry.Replica] = entry.Value;
        }

        return counter;
    }

    /// <inheritdoc/>
    public bool Equals(GCounter? other)
    {
        if (other is null)
        {
            return false;
        }

        GCounter first = _lockOrder < other._lockOrder ? this : other;
        GCounter second = ReferenceEquals(first, this) ? other : this;
        lock (first._gate)
        {
            lock (second._gate)
            {
                if (other._counts.Count != _counts.Count)
                {
                    return false;
                }

                foreach (KeyValuePair<ReplicaId, ulong> entry in _counts)
                {
                    if (!other._counts.TryGetValue(entry.Key, out ulong value) || value != entry.Value)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as GCounter);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        lock (_gate)
        {
            int hash = 0;
            foreach (KeyValuePair<ReplicaId, ulong> entry in _counts)
            {
                hash ^= HashCode.Combine(entry.Key, entry.Value);
            }

            return hash;
        }
    }

    internal List<KeyValuePair<ReplicaId, ulong>> SortedCounts()
    {
        List<KeyValuePair<ReplicaId, ulong>> list;
        lock (_gate)
        {
            list = new List<KeyValuePair<ReplicaId, ulong>>(_counts);
        }

        list.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return list;
    }

    internal static void WriteCounts(ref CrdtWriter writer, List<KeyValuePair<ReplicaId, ulong>> counts)
    {
        writer.WriteVarUInt64((ulong)counts.Count);
        foreach (KeyValuePair<ReplicaId, ulong> entry in counts)
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt64(entry.Value);
        }
    }

    internal void SetCount(ReplicaId replica, ulong value)
    {
        lock (_gate)
        {
            _counts[replica] = value;
        }
    }

    private void BufferDelta(ReplicaId replica, ulong value)
    {
        _delta ??= [];
        _delta[replica] = value;
    }
}
