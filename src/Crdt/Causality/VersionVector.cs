// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// A version vector: a map from <see cref="ReplicaId"/> to the highest contiguous
/// sequence number observed from that replica. It summarises causal history compactly and
/// is the backbone of causal CRDTs and anti-entropy.
/// </summary>
/// <remarks>
/// A version vector assumes <em>contiguous</em> per-replica history: a value of <c>n</c> for
/// replica <c>r</c> means events <c>1..n</c> from <c>r</c> have all been observed. For
/// causal contexts that must also track out-of-order events, see the dot-store machinery
/// which pairs a version vector with a "dot cloud". This type is mutable and not
/// thread-safe.
/// </remarks>
public sealed class VersionVector : IEquatable<VersionVector>, IBinaryWritable
{
    private readonly Dictionary<ReplicaId, ulong> _versions;

    /// <summary>Initializes an empty version vector.</summary>
    public VersionVector() => _versions = [];

    private VersionVector(Dictionary<ReplicaId, ulong> versions) => _versions = versions;

    /// <summary>Gets the number of replicas tracked by this vector.</summary>
    public int Count => _versions.Count;

    /// <summary>Gets a value indicating whether this vector tracks no events.</summary>
    public bool IsEmpty => _versions.Count == 0;

    /// <summary>Gets the highest contiguous sequence observed for <paramref name="replica"/>, or 0.</summary>
    /// <param name="replica">The replica to look up.</param>
    public ulong this[ReplicaId replica] => _versions.TryGetValue(replica, out ulong value) ? value : 0UL;

    /// <summary>Gets the replicas tracked by this vector.</summary>
    public IReadOnlyCollection<ReplicaId> Replicas => _versions.Keys;

    /// <summary>
    /// Advances <paramref name="replica"/>'s counter by one and returns the freshly-minted
    /// <see cref="Dot"/>. Used by a replica to stamp its own next local event.
    /// </summary>
    /// <param name="replica">The replica producing the event.</param>
    /// <returns>The new dot.</returns>
    public Dot Increment(ReplicaId replica)
    {
        ulong next = (_versions.TryGetValue(replica, out ulong current) ? current : 0UL) + 1UL;
        _versions[replica] = next;
        return new Dot(replica, next);
    }

    /// <summary>
    /// Records observation of <paramref name="dot"/> by raising the replica's counter to at
    /// least the dot's sequence.
    /// </summary>
    /// <param name="dot">The dot to observe.</param>
    /// <returns><see langword="true"/> if the vector advanced; otherwise <see langword="false"/>.</returns>
    public bool Observe(Dot dot)
    {
        if (_versions.TryGetValue(dot.Replica, out ulong current))
        {
            if (dot.Sequence > current)
            {
                _versions[dot.Replica] = dot.Sequence;
                return true;
            }

            return false;
        }

        _versions[dot.Replica] = dot.Sequence;
        return true;
    }

    /// <summary>Determines whether <paramref name="dot"/> is covered by this vector.</summary>
    /// <param name="dot">The dot to test.</param>
    /// <returns><see langword="true"/> if the replica's counter is at least the dot's sequence.</returns>
    public bool Contains(Dot dot) => this[dot.Replica] >= dot.Sequence;

    /// <summary>Joins <paramref name="other"/> into this vector by taking the per-replica maximum.</summary>
    /// <param name="other">The vector to merge in.</param>
    public void Merge(VersionVector other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<ReplicaId, ulong> entry in other._versions)
        {
            if (!_versions.TryGetValue(entry.Key, out ulong current) || entry.Value > current)
            {
                _versions[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>Compares this vector with <paramref name="other"/> under the dominance partial order.</summary>
    /// <param name="other">The vector to compare against.</param>
    /// <returns>The ordering relationship.</returns>
    public CrdtOrder Compare(VersionVector other)
    {
        Throw.IfNull(other);
        bool less = false;
        bool greater = false;

        foreach (KeyValuePair<ReplicaId, ulong> entry in _versions)
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

        foreach (KeyValuePair<ReplicaId, ulong> entry in other._versions)
        {
            if (!_versions.ContainsKey(entry.Key) && entry.Value > 0UL)
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

    /// <summary>Creates a deep, independent copy of this vector.</summary>
    /// <returns>The cloned vector.</returns>
    public VersionVector Clone() => new(new Dictionary<ReplicaId, ulong>(_versions));

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)_versions.Count);
        foreach (KeyValuePair<ReplicaId, ulong> entry in SortedEntries())
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt64(entry.Value);
        }
    }

    /// <summary>Decodes a version vector from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded vector.</returns>
    public static VersionVector ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static VersionVector Read(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var vector = new VersionVector();
        for (int i = 0; i < count; i++)
        {
            ReplicaId replica = reader.ReadReplicaId();
            ulong sequence = reader.ReadVarUInt64();
            vector._versions[replica] = sequence;
        }

        return vector;
    }

    /// <summary>Enumerates the vector's entries in canonical (replica-sorted) order.</summary>
    /// <returns>The entries ordered by replica.</returns>
    internal IEnumerable<KeyValuePair<ReplicaId, ulong>> SortedEntries()
    {
        var list = new List<KeyValuePair<ReplicaId, ulong>>(_versions);
        list.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return list;
    }

    /// <inheritdoc/>
    public bool Equals(VersionVector? other)
    {
        if (other is null || other._versions.Count != _versions.Count)
        {
            return false;
        }

        foreach (KeyValuePair<ReplicaId, ulong> entry in _versions)
        {
            if (!other._versions.TryGetValue(entry.Key, out ulong value) || value != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as VersionVector);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // Order-independent hash over the entries.
        int hash = 0;
        foreach (KeyValuePair<ReplicaId, ulong> entry in _versions)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return hash;
    }
}
