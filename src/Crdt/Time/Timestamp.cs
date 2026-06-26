// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Crdt;

/// <summary>
/// A last-writer-wins timestamp providing a deterministic total order across replicas.
/// It pairs a physical wall-clock component with a logical counter (for events sharing the
/// same physical instant) and the originating <see cref="ReplicaId"/> (to break remaining
/// ties), which together form a Hybrid Logical Clock reading.
/// </summary>
/// <remarks>
/// Generate timestamps with <see cref="HybridLogicalClock"/> so that local order is
/// monotonic and remote timestamps advance the local clock. Comparison is lexicographic:
/// wall clock, then counter, then origin.
/// </remarks>
public readonly struct Timestamp : IEquatable<Timestamp>, IComparable<Timestamp>
{
    /// <summary>Initializes a new <see cref="Timestamp"/>.</summary>
    /// <param name="wallClock">The physical component, in Unix milliseconds.</param>
    /// <param name="counter">The logical counter disambiguating same-instant events.</param>
    /// <param name="origin">The replica that produced the timestamp (final tie-breaker).</param>
    public Timestamp(long wallClock, ulong counter, ReplicaId origin)
    {
        WallClock = wallClock;
        Counter = counter;
        Origin = origin;
    }

    /// <summary>Gets the physical wall-clock component, in Unix milliseconds.</summary>
    public long WallClock { get; }

    /// <summary>Gets the logical counter for events at the same wall-clock instant.</summary>
    public ulong Counter { get; }

    /// <summary>Gets the replica that produced this timestamp.</summary>
    public ReplicaId Origin { get; }

    /// <summary>Gets the minimum (earliest) timestamp value.</summary>
    public static Timestamp MinValue => default;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Timestamp other) =>
        WallClock == other.WallClock && Counter == other.Counter && Origin.Equals(other.Origin);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Timestamp other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(WallClock, Counter, Origin);

    /// <summary>Compares two timestamps lexicographically (wall clock, counter, origin).</summary>
    /// <param name="other">The timestamp to compare against.</param>
    /// <returns>A signed ordering value.</returns>
    public int CompareTo(Timestamp other)
    {
        int byWall = WallClock.CompareTo(other.WallClock);
        if (byWall != 0)
        {
            return byWall;
        }

        int byCounter = Counter.CompareTo(other.Counter);
        return byCounter != 0 ? byCounter : Origin.CompareTo(other.Origin);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{WallClock}.{Counter}@{Origin}";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Timestamp left, Timestamp right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Timestamp left, Timestamp right) => !left.Equals(right);

    /// <summary>Less-than operator.</summary>
    public static bool operator <(Timestamp left, Timestamp right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(Timestamp left, Timestamp right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(Timestamp left, Timestamp right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(Timestamp left, Timestamp right) => left.CompareTo(right) >= 0;
}
