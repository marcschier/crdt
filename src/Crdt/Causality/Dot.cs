// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Crdt;

/// <summary>
/// A <em>dot</em> — a single, globally-unique event identifier of the form
/// <c>(replica, sequence)</c>. Every causally-tracked change in a CRDT is stamped with a
/// dot whose <see cref="Sequence"/> is monotonically increasing per <see cref="Replica"/>.
/// </summary>
/// <remarks>
/// Sequence numbers are 1-based: the first event a replica produces is sequence <c>1</c>.
/// Dots are the atoms of causal context (see <c>VersionVector</c> and <c>DotContext</c>)
/// and of dot-store CRDTs such as the observed-remove set.
/// </remarks>
public readonly struct Dot : IEquatable<Dot>, IComparable<Dot>
{
    /// <summary>Initializes a new <see cref="Dot"/>.</summary>
    /// <param name="replica">The replica that produced the event.</param>
    /// <param name="sequence">The 1-based per-replica sequence number.</param>
    public Dot(ReplicaId replica, ulong sequence)
    {
        Replica = replica;
        Sequence = sequence;
    }

    /// <summary>Gets the replica that produced the event.</summary>
    public ReplicaId Replica { get; }

    /// <summary>Gets the 1-based, per-replica sequence number of the event.</summary>
    public ulong Sequence { get; }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Dot other) => Sequence == other.Sequence && Replica.Equals(other.Replica);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Dot other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Replica, Sequence);

    /// <summary>
    /// Defines a canonical total order over dots: first by <see cref="Replica"/>, then by
    /// <see cref="Sequence"/>. This ordering drives deterministic serialization.
    /// </summary>
    /// <param name="other">The dot to compare against.</param>
    /// <returns>A signed ordering value.</returns>
    public int CompareTo(Dot other)
    {
        int byReplica = Replica.CompareTo(other.Replica);
        return byReplica != 0 ? byReplica : Sequence.CompareTo(other.Sequence);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Replica}:{Sequence}";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Dot left, Dot right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Dot left, Dot right) => !left.Equals(right);

    /// <summary>Less-than operator (canonical order).</summary>
    public static bool operator <(Dot left, Dot right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator (canonical order).</summary>
    public static bool operator >(Dot left, Dot right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator (canonical order).</summary>
    public static bool operator <=(Dot left, Dot right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator (canonical order).</summary>
    public static bool operator >=(Dot left, Dot right) => left.CompareTo(right) >= 0;
}
