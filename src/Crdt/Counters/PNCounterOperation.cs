// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// An operation-based update broadcast by a <see cref="PNCounter"/>. It carries the replica's
/// new <em>absolute</em> increment and decrement totals, so <see cref="PNCounter.Apply"/> is
/// idempotent and commutative (each side is a per-replica maximum).
/// </summary>
public readonly struct PNCounterOperation : IEquatable<PNCounterOperation>, IBinaryWritable
{
    /// <summary>Initializes a new <see cref="PNCounterOperation"/>.</summary>
    /// <param name="replica">The replica whose totals changed.</param>
    /// <param name="positive">The replica's new absolute increment total.</param>
    /// <param name="negative">The replica's new absolute decrement total.</param>
    public PNCounterOperation(ReplicaId replica, ulong positive, ulong negative)
    {
        Replica = replica;
        Positive = positive;
        Negative = negative;
    }

    /// <summary>Gets the replica whose totals changed.</summary>
    public ReplicaId Replica { get; }

    /// <summary>Gets the replica's new absolute increment total.</summary>
    public ulong Positive { get; }

    /// <summary>Gets the replica's new absolute decrement total.</summary>
    public ulong Negative { get; }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteReplicaId(Replica);
        writer.WriteVarUInt64(Positive);
        writer.WriteVarUInt64(Negative);
    }

    /// <summary>Decodes a <see cref="PNCounterOperation"/> from its binary form.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static PNCounterOperation ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static PNCounterOperation Read(ref CrdtReader reader) =>
        new(reader.ReadReplicaId(), reader.ReadVarUInt64(), reader.ReadVarUInt64());

    /// <inheritdoc/>
    public bool Equals(PNCounterOperation other) =>
        Positive == other.Positive && Negative == other.Negative && Replica.Equals(other.Replica);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PNCounterOperation other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Replica, Positive, Negative);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(PNCounterOperation left, PNCounterOperation right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(PNCounterOperation left, PNCounterOperation right) => !left.Equals(right);
}
