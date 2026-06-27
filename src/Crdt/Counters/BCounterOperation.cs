// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>The kind of absolute update carried by a <see cref="BCounterOperation"/>.</summary>
public enum BCounterOperationKind
{
    /// <summary>An absolute per-replica increment total.</summary>
    Increment,

    /// <summary>An absolute per-replica decrement total.</summary>
    Decrement,

    /// <summary>An absolute rights transfer total from one replica to another.</summary>
    Transfer,
}

/// <summary>
/// An operation-based update broadcast by a <see cref="BCounter"/>. It carries an
/// <em>absolute</em> slot or transfer total, so <see cref="BCounter.Apply"/> is idempotent
/// and commutative (the effect is a per-slot maximum).
/// </summary>
public readonly struct BCounterOperation : IEquatable<BCounterOperation>, IBinaryWritable
{
    /// <summary>Initializes an increment or decrement operation.</summary>
    /// <param name="kind">The operation kind, which must be increment or decrement.</param>
    /// <param name="replica">The replica whose slot changed.</param>
    /// <param name="value">The replica's new absolute slot total.</param>
    public BCounterOperation(BCounterOperationKind kind, ReplicaId replica, ulong value)
    {
        Kind = kind;
        Replica = replica;
        From = replica;
        To = default;
        Value = value;
    }

    /// <summary>Initializes a transfer operation.</summary>
    /// <param name="from">The replica transferring rights.</param>
    /// <param name="to">The replica receiving rights.</param>
    /// <param name="value">The new absolute transfer total for the ordered replica pair.</param>
    public BCounterOperation(ReplicaId from, ReplicaId to, ulong value)
    {
        Kind = BCounterOperationKind.Transfer;
        Replica = from;
        From = from;
        To = to;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public BCounterOperationKind Kind { get; }

    /// <summary>Gets the replica whose increment or decrement slot changed.</summary>
    public ReplicaId Replica { get; }

    /// <summary>Gets the source replica for a transfer operation.</summary>
    public ReplicaId From { get; }

    /// <summary>Gets the destination replica for a transfer operation.</summary>
    public ReplicaId To { get; }

    /// <summary>Gets the new absolute slot or transfer total.</summary>
    public ulong Value { get; }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte((byte)Kind);
        switch (Kind)
        {
            case BCounterOperationKind.Increment:
            case BCounterOperationKind.Decrement:
                writer.WriteReplicaId(Replica);
                writer.WriteVarUInt64(Value);
                break;
            case BCounterOperationKind.Transfer:
                writer.WriteReplicaId(From);
                writer.WriteReplicaId(To);
                writer.WriteVarUInt64(Value);
                break;
        }
    }

    /// <summary>Decodes a <see cref="BCounterOperation"/> from its binary form.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static BCounterOperation ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static BCounterOperation Read(ref CrdtReader reader)
    {
        var kind = (BCounterOperationKind)reader.ReadByte();
        return kind switch
        {
            BCounterOperationKind.Increment or BCounterOperationKind.Decrement =>
                new BCounterOperation(kind, reader.ReadReplicaId(), reader.ReadVarUInt64()),
            BCounterOperationKind.Transfer =>
                new BCounterOperation(reader.ReadReplicaId(), reader.ReadReplicaId(), reader.ReadVarUInt64()),
            _ => Throw.InvalidData<BCounterOperation>("Unknown bounded-counter operation kind."),
        };
    }

    /// <inheritdoc/>
    public bool Equals(BCounterOperation other) =>
        Kind == other.Kind &&
        Replica.Equals(other.Replica) &&
        From.Equals(other.From) &&
        To.Equals(other.To) &&
        Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is BCounterOperation other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Replica, From, To, Value);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(BCounterOperation left, BCounterOperation right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(BCounterOperation left, BCounterOperation right) => !left.Equals(right);
}
