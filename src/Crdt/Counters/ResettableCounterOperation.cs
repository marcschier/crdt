// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt;

/// <summary>Identifies the kind of change described by a <see cref="ResettableCounterOperation"/>.</summary>
public enum ResettableCounterOperationKind
{
    /// <summary>Adds one dotted increment or decrement contribution.</summary>
    Increment = 0,

    /// <summary>Removes every dotted contribution observed by the reset.</summary>
    Reset = 1,
}

/// <summary>
/// Describes an idempotent increment, decrement, or observed reset operation for a
/// <see cref="ResettableCounter"/>.
/// </summary>
public readonly struct ResettableCounterOperation : IBinaryWritable, IEquatable<ResettableCounterOperation>
{
    /// <summary>Initializes an increment or decrement operation.</summary>
    /// <param name="dot">The dot assigned to the contribution.</param>
    /// <param name="amount">The signed amount contributed by the operation.</param>
    public ResettableCounterOperation(Dot dot, long amount)
    {
        Kind = ResettableCounterOperationKind.Increment;
        Dot = dot;
        Amount = amount;
        Context = new DotContext();
    }

    internal ResettableCounterOperation(DotContext context)
    {
        Kind = ResettableCounterOperationKind.Reset;
        Dot = default;
        Amount = 0;
        Context = context.Clone();
    }

    /// <summary>Gets the operation kind.</summary>
    public ResettableCounterOperationKind Kind { get; }

    /// <summary>Gets the contribution dot for increment operations.</summary>
    public Dot Dot { get; }

    /// <summary>Gets the signed contribution amount for increment operations.</summary>
    public long Amount { get; }

    internal DotContext Context { get; }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte((byte)Kind);
        if (Kind == ResettableCounterOperationKind.Increment)
        {
            writer.WriteDot(Dot);
            writer.WriteVarInt64(Amount);
            return;
        }

        Context.Write(ref writer);
    }

    /// <summary>Decodes a <see cref="ResettableCounterOperation"/> from its binary representation.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static ResettableCounterOperation ReadFrom(
        ReadOnlySpan<byte> data,
        CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static ResettableCounterOperation Read(ref CrdtReader reader)
    {
        var kind = (ResettableCounterOperationKind)reader.ReadByte();
        if (kind == ResettableCounterOperationKind.Increment)
        {
            return new ResettableCounterOperation(reader.ReadDot(), reader.ReadVarInt64());
        }

        if (kind == ResettableCounterOperationKind.Reset)
        {
            return new ResettableCounterOperation(DotContext.Read(ref reader));
        }

        return Throw.InvalidData<ResettableCounterOperation>("Unknown resettable counter operation kind.");
    }

    /// <inheritdoc/>
    public bool Equals(ResettableCounterOperation other)
    {
        return Kind == other.Kind &&
            Dot.Equals(other.Dot) &&
            Amount == other.Amount &&
            ResettableCounter.ContextEquals(Context, other.Context);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ResettableCounterOperation other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = HashCode.Combine(Kind, Dot, Amount);
        foreach (KeyValuePair<ReplicaId, ulong> entry in Context.CompactEntries())
        {
            hash ^= HashCode.Combine(1, entry.Key, entry.Value);
        }

        foreach (Dot dot in Context.CloudDots())
        {
            hash ^= HashCode.Combine(2, dot);
        }

        return hash;
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(ResettableCounterOperation left, ResettableCounterOperation right) =>
        left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(ResettableCounterOperation left, ResettableCounterOperation right) =>
        !left.Equals(right);
}
