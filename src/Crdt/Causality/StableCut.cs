// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// An immutable snapshot of the causal frontier that is stable across a set of live replicas.
/// </summary>
/// <remarks>
/// A dot is stable when every vector used to form the cut covers that dot. The cut therefore
/// stores the pointwise minimum sequence per replica over all supplied vectors.
/// </remarks>
public sealed class StableCut : IEquatable<StableCut>, IBinaryWritable
{
    private readonly Dictionary<ReplicaId, ulong> _floors;

    private StableCut(Dictionary<ReplicaId, ulong> floors) => _floors = floors;

    /// <summary>Gets the number of replica floors stored by this cut.</summary>
    public int Count => _floors.Count;

    /// <summary>Gets a value indicating whether this cut covers no dots.</summary>
    public bool IsEmpty => _floors.Count == 0;

    /// <summary>Computes the stable cut for <paramref name="vectors"/> by taking their pointwise minimum.</summary>
    /// <param name="vectors">The observed frontiers of the live replicas participating in the cut.</param>
    /// <returns>The resulting stable cut.</returns>
    public static StableCut Meet(IEnumerable<VersionVector> vectors)
    {
        Throw.IfNull(vectors);

        using IEnumerator<VersionVector> enumerator = vectors.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return new StableCut([]);
        }

        VersionVector first = enumerator.Current;
        Throw.IfNull(first);

        var floors = new Dictionary<ReplicaId, ulong>();
        foreach (KeyValuePair<ReplicaId, ulong> entry in first.SortedEntries())
        {
            if (entry.Value != 0UL)
            {
                floors[entry.Key] = entry.Value;
            }
        }

        while (enumerator.MoveNext())
        {
            VersionVector vector = enumerator.Current;
            Throw.IfNull(vector);

            var replicas = new List<ReplicaId>(floors.Keys);
            foreach (ReplicaId replica in replicas)
            {
                ulong floor = vector[replica];
                if (floor == 0UL)
                {
                    floors.Remove(replica);
                }
                else if (floor < floors[replica])
                {
                    floors[replica] = floor;
                }
            }
        }

        return new StableCut(floors);
    }

    /// <summary>Determines whether <paramref name="dot"/> is covered by this stable cut.</summary>
    /// <param name="dot">The dot to test.</param>
    /// <returns><see langword="true"/> when the dot's sequence is not greater than its replica floor.</returns>
    public bool IsStable(Dot dot) => dot.Sequence <= Floor(dot.Replica);

    /// <summary>Gets the stable floor for <paramref name="replica"/>.</summary>
    /// <param name="replica">The replica to look up.</param>
    /// <returns>The stable sequence floor, or 0 when the replica has no stable events.</returns>
    public ulong Floor(ReplicaId replica) => _floors.TryGetValue(replica, out ulong floor) ? floor : 0UL;

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)_floors.Count);
        foreach (KeyValuePair<ReplicaId, ulong> entry in SortedEntries())
        {
            writer.WriteReplicaId(entry.Key);
            writer.WriteVarUInt64(entry.Value);
        }
    }

    /// <summary>Decodes a stable cut from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded stable cut.</returns>
    public static StableCut ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        int count = reader.ReadCount();
        var floors = new Dictionary<ReplicaId, ulong>();
        for (int i = 0; i < count; i++)
        {
            ReplicaId replica = reader.ReadReplicaId();
            ulong floor = reader.ReadVarUInt64();
            if (floor != 0UL)
            {
                floors[replica] = floor;
            }
        }

        return new StableCut(floors);
    }

    /// <inheritdoc/>
    public bool Equals(StableCut? other)
    {
        if (other is null || other._floors.Count != _floors.Count)
        {
            return false;
        }

        foreach (KeyValuePair<ReplicaId, ulong> entry in _floors)
        {
            if (!other._floors.TryGetValue(entry.Key, out ulong floor) || floor != entry.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as StableCut);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = 0;
        foreach (KeyValuePair<ReplicaId, ulong> entry in _floors)
        {
            hash ^= HashCode.Combine(entry.Key, entry.Value);
        }

        return hash;
    }

    private List<KeyValuePair<ReplicaId, ulong>> SortedEntries()
    {
        var entries = new List<KeyValuePair<ReplicaId, ulong>>(_floors);
        entries.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return entries;
    }
}
