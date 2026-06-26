// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>
/// The causal context for dot-store CRDTs: the set of all dots a replica has observed,
/// stored compactly as a per-replica contiguous prefix (a version vector) plus a "dot
/// cloud" of out-of-order dots not yet contiguous with the prefix.
/// </summary>
/// <remarks>
/// <para>
/// This representation is the basis of tombstone-free observed-remove CRDTs (ORSWOT): a
/// removal is recorded simply by the absence of a value-dot whose dot is nonetheless
/// present in the context. Compaction folds contiguous cloud dots back into the version
/// vector to keep the cloud bounded once causal gaps are filled.
/// </para>
/// <para>Mutable and not thread-safe.</para>
/// </remarks>
internal sealed class DotContext
{
    private readonly Dictionary<ReplicaId, ulong> _compact;
    private readonly HashSet<Dot> _cloud;

    public DotContext()
    {
        _compact = [];
        _cloud = [];
    }

    private DotContext(Dictionary<ReplicaId, ulong> compact, HashSet<Dot> cloud)
    {
        _compact = compact;
        _cloud = cloud;
    }

    public bool IsEmpty => _compact.Count == 0 && _cloud.Count == 0;

    /// <summary>Determines whether <paramref name="dot"/> has been observed.</summary>
    public bool Contains(Dot dot)
    {
        if (_compact.TryGetValue(dot.Replica, out ulong max) && dot.Sequence <= max)
        {
            return true;
        }

        return _cloud.Contains(dot);
    }

    /// <summary>
    /// Allocates the next contiguous dot for <paramref name="replica"/> and records it.
    /// Used by a replica to stamp its own new events.
    /// </summary>
    public Dot NextDot(ReplicaId replica)
    {
        ulong next = (_compact.TryGetValue(replica, out ulong current) ? current : 0UL) + 1UL;
        _compact[replica] = next;
        return new Dot(replica, next);
    }

    /// <summary>Records observation of <paramref name="dot"/>, compacting if it fills a gap.</summary>
    public void Add(Dot dot)
    {
        if (Contains(dot))
        {
            return;
        }

        _cloud.Add(dot);
        CompactReplica(dot.Replica);
    }

    /// <summary>Joins another context into this one (union of observed dots).</summary>
    public void Merge(DotContext other)
    {
        foreach (KeyValuePair<ReplicaId, ulong> entry in other._compact)
        {
            if (!_compact.TryGetValue(entry.Key, out ulong current) || entry.Value > current)
            {
                _compact[entry.Key] = entry.Value;
            }
        }

        foreach (Dot dot in other._cloud)
        {
            _cloud.Add(dot);
        }

        Compact();
    }

    private void Compact()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            // Snapshot to allow removal during iteration.
            foreach (Dot dot in new List<Dot>(_cloud))
            {
                ulong max = _compact.TryGetValue(dot.Replica, out ulong value) ? value : 0UL;
                if (dot.Sequence <= max)
                {
                    _cloud.Remove(dot);
                }
                else if (dot.Sequence == max + 1UL)
                {
                    _compact[dot.Replica] = dot.Sequence;
                    _cloud.Remove(dot);
                    changed = true;
                }
            }
        }
    }

    private void CompactReplica(ReplicaId replica)
    {
        ulong max = _compact.TryGetValue(replica, out ulong value) ? value : 0UL;
        bool changed = true;
        while (changed)
        {
            changed = false;
            var candidate = new Dot(replica, max + 1UL);
            if (_cloud.Remove(candidate))
            {
                max += 1UL;
                _compact[replica] = max;
                changed = true;
            }
        }
    }

    public DotContext Clone() =>
        new(new Dictionary<ReplicaId, ulong>(_compact), new HashSet<Dot>(_cloud));

    internal void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)_compact.Count);
        foreach (KeyValuePair<ReplicaId, ulong> entry in CompactEntries())
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt64(entry.Value);
        }

        writer.WriteVarUInt64((ulong)_cloud.Count);
        foreach (Dot dot in CloudDots())
        {
            writer.WriteDot(dot);
        }
    }

    internal static DotContext Read(ref CrdtReader reader)
    {
        var context = new DotContext();

        int compactCount = reader.ReadCount();
        for (int i = 0; i < compactCount; i++)
        {
            ReplicaId replica = reader.ReadReplicaId();
            ulong sequence = reader.ReadVarUInt64();
            context._compact[replica] = sequence;
        }

        int cloudCount = reader.ReadCount();
        for (int i = 0; i < cloudCount; i++)
        {
            context._cloud.Add(reader.ReadDot());
        }

        return context;
    }

    /// <summary>The contiguous per-replica prefix, sorted by replica for canonical serialization.</summary>
    public IEnumerable<KeyValuePair<ReplicaId, ulong>> CompactEntries()
    {
        var list = new List<KeyValuePair<ReplicaId, ulong>>(_compact);
        list.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return list;
    }

    /// <summary>The out-of-order dot cloud, sorted for canonical serialization.</summary>
    public IEnumerable<Dot> CloudDots()
    {
        var list = new List<Dot>(_cloud);
        list.Sort();
        return list;
    }
}
