// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// An operation-based increment broadcast by a <see cref="GCounter"/>. It carries the
/// replica's new <em>absolute</em> per-replica total, which makes <see cref="GCounter.Apply"/>
/// naturally idempotent and commutative (the effect is a per-replica maximum), so no causal
/// ordering or de-duplication metadata is required.
/// </summary>
public readonly struct GCounterOperation : IEquatable<GCounterOperation>, IBinaryWritable
{
    /// <summary>Initializes a new <see cref="GCounterOperation"/>.</summary>
    /// <param name="replica">The replica whose total changed.</param>
    /// <param name="value">The replica's new absolute total.</param>
    public GCounterOperation(ReplicaId replica, ulong value)
    {
        Replica = replica;
        Value = value;
    }

    /// <summary>Gets the replica whose total changed.</summary>
    public ReplicaId Replica { get; }

    /// <summary>Gets the replica's new absolute total.</summary>
    public ulong Value { get; }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteReplicaId(Replica);
        writer.WriteVarUInt64(Value);
    }

    /// <summary>Decodes a <see cref="GCounterOperation"/> from its binary form.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static GCounterOperation ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static GCounterOperation Read(ref CrdtReader reader) =>
        new(reader.ReadReplicaId(), reader.ReadVarUInt64());

    /// <inheritdoc/>
    public bool Equals(GCounterOperation other) => Value == other.Value && Replica.Equals(other.Replica);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is GCounterOperation other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Replica, Value);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(GCounterOperation left, GCounterOperation right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(GCounterOperation left, GCounterOperation right) => !left.Equals(right);
}
