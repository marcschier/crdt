// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// A two-tier handoff counter CRDT: lower-tier clients count locally and hand accumulated
/// totals to higher-tier aggregators while preserving convergence under duplicated,
/// reordered, and repeated state exchanges.
/// </summary>
/// <remarks>
/// This implementation models the Almeida-Baquero slot/token handoff protocol for the
/// common two-tier topology (<c>0</c> clients and <c>1</c> aggregators). Higher tier values
/// are also accepted, and merges between equal tiers still join by per-replica maximums, but
/// only lower-to-higher handoff compaction is performed. Mutable and not thread-safe.
/// </remarks>
public sealed partial class HandoffCounter :
    IConvergent<HandoffCounter>,
    IBinaryWritable,
    IEquatable<HandoffCounter>
{
    private readonly Dictionary<ReplicaId, ulong> _vec;
    private readonly Dictionary<ReplicaId, ulong> _below;
    private readonly Dictionary<ReplicaId, HandoffSlot> _slots;
    private readonly Dictionary<ReplicaId, HandoffToken> _tokens;
    private ulong _val;
    private ulong _sck;
    private ulong _dck;

    /// <summary>Initializes a new handoff counter for <paramref name="id"/> at <paramref name="tier"/>.</summary>
    /// <param name="id">The replica identity of this node.</param>
    /// <param name="tier">The node tier. Tier <c>0</c> is a leaf/client; larger values aggregate lower tiers.</param>
    public HandoffCounter(ReplicaId id, int tier)
    {
        if (tier < 0)
        {
            Throw.ArgumentOutOfRange(nameof(tier), "Tier must be non-negative.");
        }

        Id = id;
        Tier = tier;
        _vec = [];
        _below = [];
        _slots = [];
        _tokens = [];
    }

    private HandoffCounter(
        ReplicaId id,
        int tier,
        ulong val,
        Dictionary<ReplicaId, ulong> vec,
        Dictionary<ReplicaId, ulong> below,
        Dictionary<ReplicaId, HandoffSlot> slots,
        Dictionary<ReplicaId, HandoffToken> tokens,
        ulong sck,
        ulong dck)
    {
        Id = id;
        Tier = tier;
        _val = val;
        _vec = vec;
        _below = below;
        _slots = slots;
        _tokens = tokens;
        _sck = sck;
        _dck = dck;
        Normalize();
    }

    /// <summary>Gets this node's replica identity.</summary>
    public ReplicaId Id { get; }

    /// <summary>Gets this node's handoff tier.</summary>
    public int Tier { get; }

    /// <summary>Gets the logical counter value represented by this state.</summary>
    public ulong Value
    {
        get
        {
            ulong sum = 0;
            foreach (KeyValuePair<ReplicaId, ulong> entry in LogicalEntries())
            {
                sum += entry.Value;
            }

            return sum;
        }
    }

    internal ulong AggregatedValue => _val;

    internal int UnhandedCount => _vec.Count;

    internal int SlotCount => _slots.Count;

    internal int TokenCount => _tokens.Count;

    /// <summary>Increments this node's own contribution by <paramref name="amount"/>.</summary>
    /// <param name="amount">The positive amount to add.</param>
    public void Increment(ulong amount = 1)
    {
        if (amount == 0)
        {
            Throw.ArgumentOutOfRange(nameof(amount), "Increment amount must be positive.");
        }

        _vec[Id] = TotalFor(Id) + amount;
        _sck++;
    }

    /// <inheritdoc/>
    public void Merge(HandoffCounter other)
    {
        Throw.IfNull(other);

        MergeMax(_below, other._below);
        MergeSlots(other._slots);
        MergeTokens(other._tokens);

        CreateSlot(other);
        MergeMax(_vec, other._vec);
        FillSlot(other);
        ApplyTokens(other);
        AcceptHigherTierAcknowledgement(other);
        DiscardCoveredProtocolState();
        Normalize();
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(HandoffCounter other)
    {
        Throw.IfNull(other);
        bool less = false;
        bool greater = false;

        foreach (KeyValuePair<ReplicaId, ulong> entry in LogicalEntries())
        {
            ulong otherValue = other.TotalFor(entry.Key);
            if (entry.Value > otherValue)
            {
                greater = true;
            }
            else if (entry.Value < otherValue)
            {
                less = true;
            }
        }

        foreach (KeyValuePair<ReplicaId, ulong> entry in other.LogicalEntries())
        {
            if (TotalFor(entry.Key) == 0UL && entry.Value > 0UL)
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

    /// <inheritdoc/>
    public HandoffCounter Clone() =>
        new(
            Id,
            Tier,
            _val,
            new Dictionary<ReplicaId, ulong>(_vec),
            new Dictionary<ReplicaId, ulong>(_below),
            new Dictionary<ReplicaId, HandoffSlot>(_slots),
            new Dictionary<ReplicaId, HandoffToken>(_tokens),
            _sck,
            _dck);

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(CrdtBinary.FormatVersion);
        writer.WriteReplicaId(Id);
        writer.WriteVarUInt32((uint)Tier);
        writer.WriteVarUInt64(_val);
        writer.WriteVarUInt64(_sck);
        writer.WriteVarUInt64(_dck);
        WriteEntries(ref writer, _vec);
        WriteEntries(ref writer, _below);
        WriteSlots(ref writer);
        WriteTokens(ref writer);
    }

    /// <summary>Decodes a <see cref="HandoffCounter"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded counter.</returns>
    public static HandoffCounter ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static HandoffCounter Read(ref CrdtReader reader)
    {
        byte version = reader.ReadByte();
        if (version != CrdtBinary.FormatVersion)
        {
            Throw.InvalidData<HandoffCounter>("Unsupported handoff counter binary format version.");
        }

        ReplicaId id = reader.ReadReplicaId();
        uint tier = reader.ReadVarUInt32();
        if (tier > int.MaxValue)
        {
            Throw.InvalidData<HandoffCounter>("Encoded handoff counter tier exceeds 32-bit range.");
        }

        ulong val = reader.ReadVarUInt64();
        ulong sck = reader.ReadVarUInt64();
        ulong dck = reader.ReadVarUInt64();
        Dictionary<ReplicaId, ulong> vec = ReadEntries(ref reader);
        Dictionary<ReplicaId, ulong> below = ReadEntries(ref reader);
        Dictionary<ReplicaId, HandoffSlot> slots = ReadSlots(ref reader);
        Dictionary<ReplicaId, HandoffToken> tokens = ReadTokens(ref reader);
        return new HandoffCounter(id, (int)tier, val, vec, below, slots, tokens, sck, dck);
    }

    /// <inheritdoc/>
    public bool Equals(HandoffCounter? other) =>
        other is not null
        && Id == other.Id
        && Tier == other.Tier
        && _val == other._val
        && _sck == other._sck
        && _dck == other._dck
        && DictionaryEquals(_vec, other._vec)
        && DictionaryEquals(_below, other._below)
        && DictionaryEquals(_slots, other._slots)
        && DictionaryEquals(_tokens, other._tokens);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as HandoffCounter);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = HashCode.Combine(Id, Tier, _val, _sck, _dck);
        hash ^= DictionaryHash(_vec);
        hash ^= DictionaryHash(_below);
        hash ^= DictionaryHash(_slots);
        hash ^= DictionaryHash(_tokens);
        return hash;
    }

    internal ulong TotalFor(ReplicaId replica)
    {
        _vec.TryGetValue(replica, out ulong local);
        _below.TryGetValue(replica, out ulong handed);
        return local > handed ? local : handed;
    }

    internal IEnumerable<KeyValuePair<ReplicaId, ulong>> SortedVec() => SortedEntries(_vec);

    internal IEnumerable<KeyValuePair<ReplicaId, ulong>> SortedBelow() => SortedEntries(_below);

    internal IEnumerable<KeyValuePair<ReplicaId, HandoffSlot>> SortedSlots() => SortedEntries(_slots);

    internal IEnumerable<KeyValuePair<ReplicaId, HandoffToken>> SortedTokens() => SortedEntries(_tokens);

    private static void MergeMax(Dictionary<ReplicaId, ulong> target, Dictionary<ReplicaId, ulong> source)
    {
        foreach (KeyValuePair<ReplicaId, ulong> entry in source)
        {
            if (!target.TryGetValue(entry.Key, out ulong current) || entry.Value > current)
            {
                target[entry.Key] = entry.Value;
            }
        }
    }

    private static bool DictionaryEquals<TValue>(
        Dictionary<ReplicaId, TValue> left,
        Dictionary<ReplicaId, TValue> right)
        where TValue : IEquatable<TValue>
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<ReplicaId, TValue> entry in left)
        {
            if (!right.TryGetValue(entry.Key, out TValue? value) || !entry.Value.Equals(value))
            {
                return false;
            }
        }

        return true;
    }

    private static int DictionaryHash<TValue>(Dictionary<ReplicaId, TValue> dictionary)
    {
        int hash = 0;
        foreach (KeyValuePair<ReplicaId, TValue> entry in dictionary)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return hash;
    }

    private static List<KeyValuePair<ReplicaId, TValue>> SortedEntries<TValue>(
        Dictionary<ReplicaId, TValue> dictionary)
    {
        var list = new List<KeyValuePair<ReplicaId, TValue>>(dictionary);
        list.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return list;
    }

    private static void WriteEntries(ref CrdtWriter writer, Dictionary<ReplicaId, ulong> entries)
    {
        writer.WriteVarUInt64((ulong)entries.Count);
        foreach (KeyValuePair<ReplicaId, ulong> entry in SortedEntries(entries))
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt64(entry.Value);
        }
    }

    private static Dictionary<ReplicaId, ulong> ReadEntries(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var entries = new Dictionary<ReplicaId, ulong>();
        for (int i = 0; i < count; i++)
        {
            entries[reader.ReadReplicaId()] = reader.ReadVarUInt64();
        }

        return entries;
    }

    private void CreateSlot(HandoffCounter other)
    {
        if (Tier <= other.Tier)
        {
            return;
        }

        ulong observed = other.TotalFor(other.Id);
        ulong current = TotalFor(other.Id);
        if (observed <= current)
        {
            return;
        }

        _dck++;
        _slots[other.Id] = new HandoffSlot(other.Tier, other._sck, _dck);
        Aggregate(other.Id, observed, current);
    }

    private void FillSlot(HandoffCounter other)
    {
        if (Tier >= other.Tier || !other._slots.TryGetValue(Id, out HandoffSlot slot))
        {
            return;
        }

        ulong total = TotalFor(Id);
        _tokens[other.Id] = new HandoffToken(other.Tier, total, slot.SourceClock, slot.DestinationClock);
    }

    private void ApplyTokens(HandoffCounter other)
    {
        if (Tier <= other.Tier || !other._tokens.TryGetValue(Id, out HandoffToken token))
        {
            return;
        }

        Aggregate(other.Id, token.Value, TotalFor(other.Id));
    }

    private void AcceptHigherTierAcknowledgement(HandoffCounter other)
    {
        if (Tier >= other.Tier)
        {
            return;
        }

        ulong acknowledged = other.TotalFor(Id);
        if (acknowledged > 0UL)
        {
            Aggregate(Id, acknowledged, TotalFor(Id));
        }
    }

    private void Aggregate(ReplicaId replica, ulong value, ulong logicalCurrent)
    {
        if (!_below.TryGetValue(replica, out ulong current) || value > current)
        {
            _below[replica] = value;
            _val += value - logicalCurrent;
        }
    }

    private void DiscardCoveredProtocolState()
    {
        var removeSlots = new List<ReplicaId>();
        foreach (KeyValuePair<ReplicaId, HandoffSlot> entry in _slots)
        {
            if (TotalFor(entry.Key) >= entry.Value.SourceClock)
            {
                removeSlots.Add(entry.Key);
            }
        }

        foreach (ReplicaId replica in removeSlots)
        {
            _slots.Remove(replica);
        }

        var removeTokens = new List<ReplicaId>();
        foreach (KeyValuePair<ReplicaId, HandoffToken> entry in _tokens)
        {
            if (_below.TryGetValue(Id, out ulong acknowledged) && acknowledged >= entry.Value.Value)
            {
                removeTokens.Add(entry.Key);
            }
        }

        foreach (ReplicaId replica in removeTokens)
        {
            _tokens.Remove(replica);
        }
    }

    private void Normalize()
    {
        var remove = new List<ReplicaId>();
        foreach (KeyValuePair<ReplicaId, ulong> entry in _vec)
        {
            if (_below.TryGetValue(entry.Key, out ulong handed) && handed >= entry.Value)
            {
                remove.Add(entry.Key);
            }
        }

        foreach (ReplicaId replica in remove)
        {
            _vec.Remove(replica);
        }

        ulong aggregate = _val;
        foreach (KeyValuePair<ReplicaId, ulong> entry in _below)
        {
            if (entry.Value > aggregate)
            {
                aggregate = entry.Value;
            }
        }

        _val = aggregate;
    }

    private IEnumerable<KeyValuePair<ReplicaId, ulong>> LogicalEntries()
    {
        var keys = new HashSet<ReplicaId>(_vec.Keys);
        keys.UnionWith(_below.Keys);
        var sorted = new List<ReplicaId>(keys);
        sorted.Sort();
        foreach (ReplicaId replica in sorted)
        {
            ulong total = TotalFor(replica);
            if (total > 0UL)
            {
                yield return new KeyValuePair<ReplicaId, ulong>(replica, total);
            }
        }
    }

    private void MergeSlots(Dictionary<ReplicaId, HandoffSlot> slots)
    {
        foreach (KeyValuePair<ReplicaId, HandoffSlot> entry in slots)
        {
            if (!_slots.TryGetValue(entry.Key, out HandoffSlot current)
                || entry.Value.CompareTo(current) > 0)
            {
                _slots[entry.Key] = entry.Value;
            }
        }
    }

    private void MergeTokens(Dictionary<ReplicaId, HandoffToken> tokens)
    {
        foreach (KeyValuePair<ReplicaId, HandoffToken> entry in tokens)
        {
            if (!_tokens.TryGetValue(entry.Key, out HandoffToken current)
                || entry.Value.CompareTo(current) > 0)
            {
                _tokens[entry.Key] = entry.Value;
            }
        }
    }

    private void WriteSlots(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)_slots.Count);
        foreach (KeyValuePair<ReplicaId, HandoffSlot> entry in SortedSlots())
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt32((uint)entry.Value.Tier);
            writer.WriteVarUInt64(entry.Value.SourceClock);
            writer.WriteVarUInt64(entry.Value.DestinationClock);
        }
    }

    private void WriteTokens(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)_tokens.Count);
        foreach (KeyValuePair<ReplicaId, HandoffToken> entry in SortedTokens())
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt32((uint)entry.Value.Tier);
            writer.WriteVarUInt64(entry.Value.Value);
            writer.WriteVarUInt64(entry.Value.SourceClock);
            writer.WriteVarUInt64(entry.Value.DestinationClock);
        }
    }

    private static Dictionary<ReplicaId, HandoffSlot> ReadSlots(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var slots = new Dictionary<ReplicaId, HandoffSlot>();
        for (int i = 0; i < count; i++)
        {
            ReplicaId source = reader.ReadReplicaId();
            uint tier = reader.ReadVarUInt32();
            if (tier > int.MaxValue)
            {
                Throw.InvalidData<HandoffCounter>("Encoded handoff slot tier exceeds 32-bit range.");
            }

            slots[source] = new HandoffSlot((int)tier, reader.ReadVarUInt64(), reader.ReadVarUInt64());
        }

        return slots;
    }

    private static Dictionary<ReplicaId, HandoffToken> ReadTokens(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var tokens = new Dictionary<ReplicaId, HandoffToken>();
        for (int i = 0; i < count; i++)
        {
            ReplicaId destination = reader.ReadReplicaId();
            uint tier = reader.ReadVarUInt32();
            if (tier > int.MaxValue)
            {
                Throw.InvalidData<HandoffCounter>("Encoded handoff token tier exceeds 32-bit range.");
            }

            tokens[destination] =
                new HandoffToken((int)tier, reader.ReadVarUInt64(), reader.ReadVarUInt64(), reader.ReadVarUInt64());
        }

        return tokens;
    }
}

internal readonly struct HandoffSlot : IEquatable<HandoffSlot>, IComparable<HandoffSlot>
{
    public HandoffSlot(int tier, ulong sourceClock, ulong destinationClock)
    {
        Tier = tier;
        SourceClock = sourceClock;
        DestinationClock = destinationClock;
    }

    public int Tier { get; }

    public ulong SourceClock { get; }

    public ulong DestinationClock { get; }

    public int CompareTo(HandoffSlot other)
    {
        int result = DestinationClock.CompareTo(other.DestinationClock);
        if (result != 0)
        {
            return result;
        }

        result = SourceClock.CompareTo(other.SourceClock);
        return result != 0 ? result : Tier.CompareTo(other.Tier);
    }

    public bool Equals(HandoffSlot other) =>
        Tier == other.Tier && SourceClock == other.SourceClock && DestinationClock == other.DestinationClock;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is HandoffSlot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Tier, SourceClock, DestinationClock);
}

internal readonly struct HandoffToken : IEquatable<HandoffToken>, IComparable<HandoffToken>
{
    public HandoffToken(int tier, ulong value, ulong sourceClock, ulong destinationClock)
    {
        Tier = tier;
        Value = value;
        SourceClock = sourceClock;
        DestinationClock = destinationClock;
    }

    public int Tier { get; }

    public ulong Value { get; }

    public ulong SourceClock { get; }

    public ulong DestinationClock { get; }

    public int CompareTo(HandoffToken other)
    {
        int result = Value.CompareTo(other.Value);
        if (result != 0)
        {
            return result;
        }

        result = DestinationClock.CompareTo(other.DestinationClock);
        if (result != 0)
        {
            return result;
        }

        result = SourceClock.CompareTo(other.SourceClock);
        return result != 0 ? result : Tier.CompareTo(other.Tier);
    }

    public bool Equals(HandoffToken other) =>
        Tier == other.Tier
        && Value == other.Value
        && SourceClock == other.SourceClock
        && DestinationClock == other.DestinationClock;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is HandoffToken other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Tier, Value, SourceClock, DestinationClock);
}
