// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Crdt;

/// <summary>
/// A Lamport timestamp for moves in a <see cref="ReplicatedTree"/>.
/// </summary>
public readonly record struct MoveTimestamp : IComparable<MoveTimestamp>
{
    /// <summary>Initializes a new <see cref="MoveTimestamp"/>.</summary>
    /// <param name="counter">The Lamport counter.</param>
    /// <param name="replica">The replica that produced the timestamp.</param>
    public MoveTimestamp(ulong counter, ReplicaId replica)
    {
        Counter = counter;
        Replica = replica;
    }

    /// <summary>Gets the Lamport counter.</summary>
    public ulong Counter { get; }

    /// <summary>Gets the replica that produced the timestamp.</summary>
    public ReplicaId Replica { get; }

    /// <inheritdoc/>
    public int CompareTo(MoveTimestamp other)
    {
        int byCounter = Counter.CompareTo(other.Counter);
        return byCounter != 0 ? byCounter : Replica.CompareTo(other.Replica);
    }

    /// <summary>Writes this timestamp to the binary writer.</summary>
    /// <param name="writer">The destination writer.</param>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64(Counter);
        writer.WriteReplicaId(Replica);
    }

    /// <summary>Reads a timestamp from the binary reader.</summary>
    /// <param name="reader">The source reader.</param>
    /// <returns>The decoded timestamp.</returns>
    public static MoveTimestamp Read(ref CrdtReader reader) => new(reader.ReadVarUInt64(), reader.ReadReplicaId());

    /// <summary>Less-than operator.</summary>
    public static bool operator <(MoveTimestamp left, MoveTimestamp right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    public static bool operator >(MoveTimestamp left, MoveTimestamp right) => left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    public static bool operator <=(MoveTimestamp left, MoveTimestamp right) => left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    public static bool operator >=(MoveTimestamp left, MoveTimestamp right) => left.CompareTo(right) >= 0;
}

/// <summary>
/// A move operation for <see cref="ReplicatedTree"/>.
/// </summary>
public readonly record struct TreeMoveOperation : IEquatable<TreeMoveOperation>, IBinaryWritable
{
    /// <summary>Initializes a new <see cref="TreeMoveOperation"/>.</summary>
    /// <param name="timestamp">The operation timestamp.</param>
    /// <param name="child">The moved child node id.</param>
    /// <param name="newParent">The new parent node id.</param>
    /// <param name="meta">The metadata to store on the moved node.</param>
    public TreeMoveOperation(MoveTimestamp timestamp, string child, string newParent, string meta)
    {
        Throw.IfNull(child);
        Throw.IfNull(newParent);
        Throw.IfNull(meta);
        Timestamp = timestamp;
        Child = child;
        NewParent = newParent;
        Meta = meta;
    }

    /// <summary>Gets the operation timestamp.</summary>
    public MoveTimestamp Timestamp { get; }

    /// <summary>Gets the moved child node id.</summary>
    public string Child { get; }

    /// <summary>Gets the new parent node id.</summary>
    public string NewParent { get; }

    /// <summary>Gets the metadata to store on the moved node.</summary>
    public string Meta { get; }

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        Timestamp.Write(ref writer);
        writer.WriteString(Child);
        writer.WriteString(NewParent);
        writer.WriteString(Meta);
    }

    /// <summary>Serializes the operation into <paramref name="output"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    public void WriteTo(IBufferWriter<byte> output)
    {
        Throw.IfNull(output);
        var writer = new CrdtWriter(output);
        Write(ref writer);
    }

    /// <summary>Reads an operation from the binary reader.</summary>
    /// <param name="reader">The source reader.</param>
    /// <returns>The decoded operation.</returns>
    public static TreeMoveOperation Read(ref CrdtReader reader)
    {
        MoveTimestamp timestamp = MoveTimestamp.Read(ref reader);
        string child = reader.ReadString() ?? Throw.InvalidData<string>("Move child cannot be null.");
        string newParent = reader.ReadString() ?? Throw.InvalidData<string>("Move parent cannot be null.");
        string meta = reader.ReadString() ?? Throw.InvalidData<string>("Move metadata cannot be null.");
        return new TreeMoveOperation(timestamp, child, newParent, meta);
    }

    /// <summary>Decodes an operation from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static TreeMoveOperation ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    /// <summary>Decodes an operation from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static TreeMoveOperation ReadFrom(byte[] data, CrdtReaderOptions? options = null)
    {
        Throw.IfNull(data);
        return ReadFrom(data.AsSpan(), options);
    }

}
