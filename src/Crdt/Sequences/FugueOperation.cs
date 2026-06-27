// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>Identifies the kind of a <see cref="FugueOperation{T}"/>.</summary>
public enum FugueOperationKind
{
    /// <summary>An insertion of a new tree node.</summary>
    Insert,

    /// <summary>A deletion (tombstone) of an existing node.</summary>
    Delete,
}

/// <summary>Identifies which side of its parent a Fugue node belongs to.</summary>
public enum FugueSide
{
    /// <summary>The node is in the parent's left subtree.</summary>
    Left,

    /// <summary>The node is in the parent's right subtree.</summary>
    Right,
}

/// <summary>
/// An operation broadcast by a <see cref="FugueSequence{T}"/>. Insert operations carry the
/// complete tree node, while delete operations carry the tombstoned node id.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct FugueOperation<T>
{
    private FugueOperation(FugueOperationKind kind, Dot id, Dot parentId, FugueSide side, T? value)
    {
        Kind = kind;
        Id = id;
        ParentId = parentId;
        Side = side;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public FugueOperationKind Kind { get; }

    /// <summary>Gets the id of the node this operation concerns.</summary>
    public Dot Id { get; }

    /// <summary>Gets the parent id for insert operations, or the root sentinel.</summary>
    public Dot ParentId { get; }

    /// <summary>Gets the side of the parent for insert operations.</summary>
    public FugueSide Side { get; }

    /// <summary>Gets the inserted value for insert operations; otherwise <see langword="default"/>.</summary>
    public T? Value { get; }

    /// <summary>Creates an insert operation.</summary>
    /// <param name="id">The new node's id.</param>
    /// <param name="parentId">The id of the parent node, or the root sentinel.</param>
    /// <param name="side">The side of the parent on which the node is inserted.</param>
    /// <param name="value">The inserted value.</param>
    /// <returns>The insert operation.</returns>
    public static FugueOperation<T> Insert(Dot id, Dot parentId, FugueSide side, T value) =>
        new(FugueOperationKind.Insert, id, parentId, side, value);

    /// <summary>Creates a delete operation.</summary>
    /// <param name="id">The id of the node to delete.</param>
    /// <returns>The delete operation.</returns>
    public static FugueOperation<T> Delete(Dot id) =>
        new(FugueOperationKind.Delete, id, default, FugueSide.Left, default);

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        writer.WriteDot(Id);
        if (Kind == FugueOperationKind.Insert)
        {
            writer.WriteDot(ParentId);
            writer.WriteByte((byte)Side);
            serializer.Write(ref writer, Value!);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static FugueOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return ReadFrom(ref reader, serializer);
    }

    [SuppressMessage("Usage", "CA2225", Justification = "Factory methods Insert/Delete are provided.")]
    internal static FugueOperation<T> ReadFrom(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        var kind = (FugueOperationKind)reader.ReadByte();
        Dot id = reader.ReadDot();
        if (kind == FugueOperationKind.Insert)
        {
            Dot parentId = reader.ReadDot();
            var side = (FugueSide)reader.ReadByte();
            return Insert(id, parentId, side, serializer.Read(ref reader));
        }

        return Delete(id);
    }
}
