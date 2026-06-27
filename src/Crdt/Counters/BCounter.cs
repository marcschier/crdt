// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A bounded counter (B-Counter): an escrow CRDT that permits increments, decrements, and
/// rights transfers while preserving a configured lower bound.
/// </summary>
/// <remarks>
/// Replicas may decrement only rights they currently own. Merging takes the per-replica
/// maximum of increment and decrement totals and the per-pair maximum of transfer totals.
/// Replicas that merge must have the same <see cref="Min"/> value. Mutable and not thread-safe.
/// </remarks>
public sealed class BCounter :
    IConvergent<BCounter>,
    IOperationConvergent<BCounterOperation>,
    IBinaryWritable,
    IEquatable<BCounter>
{
    private readonly Dictionary<ReplicaId, ulong> _inc;
    private readonly Dictionary<ReplicaId, ulong> _dec;
    private readonly Dictionary<(ReplicaId From, ReplicaId To), ulong> _transfers;

    /// <summary>Initializes an empty bounded counter.</summary>
    /// <param name="min">The lower bound that all valid histories preserve.</param>
    public BCounter(long min = 0)
    {
        Min = min;
        _inc = [];
        _dec = [];
        _transfers = [];
    }

    private BCounter(
        long min,
        Dictionary<ReplicaId, ulong> inc,
        Dictionary<ReplicaId, ulong> dec,
        Dictionary<(ReplicaId From, ReplicaId To), ulong> transfers)
    {
        Min = min;
        _inc = inc;
        _dec = dec;
        _transfers = transfers;
    }

    /// <summary>Gets the lower bound preserved by all successful decrements.</summary>
    public long Min { get; }

    /// <summary>Gets the counter's value: <see cref="Min"/> plus increments minus decrements.</summary>
    public long Value => unchecked(Min + (long)Sum(_inc) - (long)Sum(_dec));

    /// <summary>Gets the increment total accumulated by <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica to look up.</param>
    public ulong IncrementOf(ReplicaId replica) => Get(_inc, replica);

    /// <summary>Gets the decrement total accumulated by <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica to look up.</param>
    public ulong DecrementOf(ReplicaId replica) => Get(_dec, replica);

    /// <summary>Gets the total rights transferred from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="from">The source replica.</param>
    /// <param name="to">The destination replica.</param>
    public ulong TransferOf(ReplicaId from, ReplicaId to) => Get(_transfers, (from, to));

    /// <summary>Gets the rights currently owned by <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica to look up.</param>
    /// <returns>The rights currently available for local decrements or transfers.</returns>
    public ulong LocalRights(ReplicaId replica)
    {
        ulong available = unchecked(Get(_inc, replica) + IncomingTransfers(replica));
        ulong spent = unchecked(Get(_dec, replica) + OutgoingTransfers(replica));
        return available >= spent ? available - spent : 0UL;
    }

    /// <summary>Increments <paramref name="replica"/>'s rights and returns the operation to broadcast.</summary>
    /// <param name="replica">The local replica performing the increment.</param>
    /// <param name="amount">The amount to add (must be positive).</param>
    /// <returns>The operation describing the new absolute increment total.</returns>
    public BCounterOperation Increment(ReplicaId replica, ulong amount = 1)
    {
        if (amount == 0)
        {
            Throw.ArgumentOutOfRange(nameof(amount), "Increment amount must be positive.");
        }

        ulong next = unchecked(Get(_inc, replica) + amount);
        _inc[replica] = next;
        return new BCounterOperation(BCounterOperationKind.Increment, replica, next);
    }

    /// <summary>Attempts to decrement the counter using rights owned by <paramref name="replica"/>.</summary>
    /// <param name="replica">The local replica performing the decrement.</param>
    /// <param name="amount">The amount to subtract (must be positive).</param>
    /// <param name="operation">The operation to broadcast when the decrement succeeds.</param>
    /// <returns><see langword="true"/> if sufficient local rights existed; otherwise <see langword="false"/>.</returns>
    public bool TryDecrement(ReplicaId replica, ulong amount, out BCounterOperation operation)
    {
        if (amount == 0)
        {
            Throw.ArgumentOutOfRange(nameof(amount), "Decrement amount must be positive.");
        }

        if (LocalRights(replica) < amount)
        {
            operation = default;
            return false;
        }

        ulong next = unchecked(Get(_dec, replica) + amount);
        _dec[replica] = next;
        operation = new BCounterOperation(BCounterOperationKind.Decrement, replica, next);
        return true;
    }

    /// <summary>Attempts to transfer decrement rights between replicas.</summary>
    /// <param name="from">The replica transferring rights.</param>
    /// <param name="to">The replica receiving rights.</param>
    /// <param name="amount">The amount of rights to transfer (must be positive).</param>
    /// <param name="operation">The operation to broadcast when the transfer succeeds.</param>
    /// <returns><see langword="true"/> if sufficient local rights existed; otherwise <see langword="false"/>.</returns>
    public bool TryTransfer(ReplicaId from, ReplicaId to, ulong amount, out BCounterOperation operation)
    {
        if (amount == 0)
        {
            Throw.ArgumentOutOfRange(nameof(amount), "Transfer amount must be positive.");
        }

        if (LocalRights(from) < amount)
        {
            operation = default;
            return false;
        }

        (ReplicaId From, ReplicaId To) key = (from, to);
        ulong next = unchecked(Get(_transfers, key) + amount);
        _transfers[key] = next;
        operation = new BCounterOperation(from, to, next);
        return true;
    }

    /// <inheritdoc/>
    public void Merge(BCounter other)
    {
        Throw.IfNull(other);
        EnsureSameMin(other);
        MergeMap(_inc, other._inc);
        MergeMap(_dec, other._dec);
        MergeMap(_transfers, other._transfers);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(BCounter other)
    {
        Throw.IfNull(other);
        EnsureSameMin(other);
        return CombineOrders(
            CompareMap(_inc, other._inc),
            CompareMap(_dec, other._dec),
            CompareMap(_transfers, other._transfers));
    }

    /// <inheritdoc/>
    public BCounter Clone() =>
        new(Min, new Dictionary<ReplicaId, ulong>(_inc), new Dictionary<ReplicaId, ulong>(_dec),
            new Dictionary<(ReplicaId From, ReplicaId To), ulong>(_transfers));

    /// <inheritdoc/>
    public bool Apply(BCounterOperation operation)
    {
        switch (operation.Kind)
        {
            case BCounterOperationKind.Increment:
                return ApplyMax(_inc, operation.Replica, operation.Value);
            case BCounterOperationKind.Decrement:
                return ApplyMax(_dec, operation.Replica, operation.Value);
            case BCounterOperationKind.Transfer:
                return ApplyMax(_transfers, (operation.From, operation.To), operation.Value);
            default:
                return false;
        }
    }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarInt64(Min);
        WriteMap(ref writer, SortedEntries(_inc));
        WriteMap(ref writer, SortedEntries(_dec));
        writer.WriteVarUInt64((ulong)_transfers.Count);
        foreach (KeyValuePair<(ReplicaId From, ReplicaId To), ulong> entry in SortedTransfers())
        {
            writer.WriteReplicaId(entry.Key.From);
            writer.WriteReplicaId(entry.Key.To);
            writer.WriteVarUInt64(entry.Value);
        }
    }

    /// <summary>Decodes a <see cref="BCounter"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded counter.</returns>
    public static BCounter ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static BCounter Read(ref CrdtReader reader)
    {
        var counter = new BCounter(reader.ReadVarInt64());
        ReadMap(ref reader, counter._inc);
        ReadMap(ref reader, counter._dec);

        int transferCount = reader.ReadCount();
        for (int i = 0; i < transferCount; i++)
        {
            ReplicaId from = reader.ReadReplicaId();
            ReplicaId to = reader.ReadReplicaId();
            counter._transfers[(from, to)] = reader.ReadVarUInt64();
        }

        return counter;
    }

    /// <summary>Serializes this counter to its canonical JSON representation.</summary>
    /// <returns>The JSON string.</returns>
    public string ToJson()
    {
        var dto = new BCounterDto(Min, ToEntryArray(_inc), ToEntryArray(_dec), ToTransferArray());
        return JsonSerializer.Serialize(dto, CrdtBCounterJson.Default.BCounterDto);
    }

    /// <summary>Deserializes a <see cref="BCounter"/> from JSON.</summary>
    /// <param name="json">The JSON string.</param>
    /// <returns>The decoded counter.</returns>
    public static BCounter FromJson(string json)
    {
        Throw.IfNull(json);
        BCounterDto? dto = JsonSerializer.Deserialize(json, CrdtBCounterJson.Default.BCounterDto);
        if (dto is null)
        {
            return new BCounter();
        }

        var counter = new BCounter(dto.Min);
        FromEntryArray(dto.I, counter._inc);
        FromEntryArray(dto.D, counter._dec);
        foreach (BCounterTransferDto entry in dto.T)
        {
            counter._transfers[(entry.From, entry.To)] = entry.Value;
        }

        return counter;
    }

    /// <inheritdoc/>
    public bool Equals(BCounter? other) =>
        other is not null &&
        Min == other.Min &&
        MapEquals(_inc, other._inc) &&
        MapEquals(_dec, other._dec) &&
        MapEquals(_transfers, other._transfers);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as BCounter);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = HashCode.Combine(Min);
        foreach (KeyValuePair<ReplicaId, ulong> entry in SortedEntries(_inc))
        {
            hash ^= HashCode.Combine(1, entry.Key, entry.Value);
        }

        foreach (KeyValuePair<ReplicaId, ulong> entry in SortedEntries(_dec))
        {
            hash ^= HashCode.Combine(2, entry.Key, entry.Value);
        }

        foreach (KeyValuePair<(ReplicaId From, ReplicaId To), ulong> entry in SortedTransfers())
        {
            hash ^= HashCode.Combine(3, entry.Key.From, entry.Key.To, entry.Value);
        }

        return hash;
    }

    private static ulong Get<TKey>(Dictionary<TKey, ulong> map, TKey key)
        where TKey : notnull => map.TryGetValue(key, out ulong value) ? value : 0UL;

    private static ulong Sum<TKey>(Dictionary<TKey, ulong> map)
        where TKey : notnull
    {
        ulong sum = 0;
        foreach (ulong value in map.Values)
        {
            sum = unchecked(sum + value);
        }

        return sum;
    }

    private static bool ApplyMax<TKey>(Dictionary<TKey, ulong> map, TKey key, ulong value)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out ulong current) || value > current)
        {
            map[key] = value;
            return true;
        }

        return false;
    }

    private static void MergeMap<TKey>(Dictionary<TKey, ulong> target, Dictionary<TKey, ulong> source)
        where TKey : notnull
    {
        foreach (KeyValuePair<TKey, ulong> entry in source)
        {
            ApplyMax(target, entry.Key, entry.Value);
        }
    }

    private static CrdtOrder CompareMap<TKey>(Dictionary<TKey, ulong> left, Dictionary<TKey, ulong> right)
        where TKey : notnull
    {
        bool less = false;
        bool greater = false;

        foreach (KeyValuePair<TKey, ulong> entry in left)
        {
            ulong rightValue = Get(right, entry.Key);
            if (entry.Value > rightValue)
            {
                greater = true;
            }
            else if (entry.Value < rightValue)
            {
                less = true;
            }
        }

        foreach (KeyValuePair<TKey, ulong> entry in right)
        {
            if (!left.ContainsKey(entry.Key) && entry.Value > 0UL)
            {
                less = true;
            }
        }

        return ToOrder(less, greater);
    }

    private static CrdtOrder CombineOrders(params CrdtOrder[] orders)
    {
        bool less = false;
        bool greater = false;
        foreach (CrdtOrder order in orders)
        {
            less |= order == CrdtOrder.Less || order == CrdtOrder.Concurrent;
            greater |= order == CrdtOrder.Greater || order == CrdtOrder.Concurrent;
        }

        return ToOrder(less, greater);
    }

    private static CrdtOrder ToOrder(bool less, bool greater) =>
        (less, greater) switch
        {
            (true, true) => CrdtOrder.Concurrent,
            (true, false) => CrdtOrder.Less,
            (false, true) => CrdtOrder.Greater,
            _ => CrdtOrder.Equal,
        };

    private static bool MapEquals<TKey>(Dictionary<TKey, ulong> left, Dictionary<TKey, ulong> right)
        where TKey : notnull
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<TKey, ulong> entry in left)
        {
            if (!right.TryGetValue(entry.Key, out ulong value) || value != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static List<KeyValuePair<ReplicaId, ulong>> SortedEntries(Dictionary<ReplicaId, ulong> map)
    {
        var list = new List<KeyValuePair<ReplicaId, ulong>>(map);
        list.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return list;
    }

    private List<KeyValuePair<(ReplicaId From, ReplicaId To), ulong>> SortedTransfers()
    {
        var list = new List<KeyValuePair<(ReplicaId From, ReplicaId To), ulong>>(_transfers);
        list.Sort(static (left, right) =>
        {
            int from = left.Key.From.CompareTo(right.Key.From);
            return from != 0 ? from : left.Key.To.CompareTo(right.Key.To);
        });
        return list;
    }

    private static void WriteMap(ref CrdtWriter writer, List<KeyValuePair<ReplicaId, ulong>> entries)
    {
        writer.WriteVarUInt64((ulong)entries.Count);
        foreach (KeyValuePair<ReplicaId, ulong> entry in entries)
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt64(entry.Value);
        }
    }

    private static void ReadMap(ref CrdtReader reader, Dictionary<ReplicaId, ulong> map)
    {
        int count = reader.ReadCount();
        for (int i = 0; i < count; i++)
        {
            map[reader.ReadReplicaId()] = reader.ReadVarUInt64();
        }
    }

    private static BCounterEntryDto[] ToEntryArray(Dictionary<ReplicaId, ulong> map)
    {
        var entries = new BCounterEntryDto[map.Count];
        int i = 0;
        foreach (KeyValuePair<ReplicaId, ulong> entry in SortedEntries(map))
        {
            entries[i++] = new BCounterEntryDto(entry.Key, entry.Value);
        }

        return entries;
    }

    private static void FromEntryArray(BCounterEntryDto[] entries, Dictionary<ReplicaId, ulong> map)
    {
        foreach (BCounterEntryDto entry in entries)
        {
            map[entry.Replica] = entry.Value;
        }
    }

    private BCounterTransferDto[] ToTransferArray()
    {
        var entries = new BCounterTransferDto[_transfers.Count];
        int i = 0;
        foreach (KeyValuePair<(ReplicaId From, ReplicaId To), ulong> entry in SortedTransfers())
        {
            entries[i++] = new BCounterTransferDto(entry.Key.From, entry.Key.To, entry.Value);
        }

        return entries;
    }

    private ulong IncomingTransfers(ReplicaId replica)
    {
        ulong sum = 0;
        foreach (KeyValuePair<(ReplicaId From, ReplicaId To), ulong> entry in _transfers)
        {
            if (entry.Key.To == replica)
            {
                sum = unchecked(sum + entry.Value);
            }
        }

        return sum;
    }

    private ulong OutgoingTransfers(ReplicaId replica)
    {
        ulong sum = 0;
        foreach (KeyValuePair<(ReplicaId From, ReplicaId To), ulong> entry in _transfers)
        {
            if (entry.Key.From == replica)
            {
                sum = unchecked(sum + entry.Value);
            }
        }

        return sum;
    }

    private void EnsureSameMin(BCounter other)
    {
        if (Min != other.Min)
        {
            throw new InvalidOperationException("Bounded counters with different lower bounds cannot be merged.");
        }
    }
}
