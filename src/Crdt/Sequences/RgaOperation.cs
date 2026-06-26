// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>Identifies the kind of an <see cref="RgaOperation{T}"/>.</summary>
public enum RgaOperationKind
{
    /// <summary>An insertion of a new element.</summary>
    Insert,

    /// <summary>A deletion (tombstone) of an existing element.</summary>
    Delete,
}

/// <summary>
/// An operation broadcast by an <see cref="Rga{T}"/>: either an insertion of a new element
/// (identified by its own <see cref="Id"/> and the <see cref="Parent"/> it was inserted
/// after) or a deletion of an existing element by id. Applying operations is idempotent and
/// order-tolerant: an element only becomes visible once its ancestors are present.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct RgaOperation<T>
{
    private RgaOperation(RgaOperationKind kind, Dot id, Dot parent, T? value)
    {
        Kind = kind;
        Id = id;
        Parent = parent;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public RgaOperationKind Kind { get; }

    /// <summary>Gets the id of the element this operation concerns.</summary>
    public Dot Id { get; }

    /// <summary>Gets the id of the element the new element was inserted after (insert only).</summary>
    public Dot Parent { get; }

    /// <summary>Gets the inserted value (insert only; default for deletes).</summary>
    public T? Value { get; }

    /// <summary>Creates an insert operation.</summary>
    /// <param name="id">The new element's id.</param>
    /// <param name="parent">The id of the element it is inserted after.</param>
    /// <param name="value">The inserted value.</param>
    /// <returns>The insert operation.</returns>
    public static RgaOperation<T> Insert(Dot id, Dot parent, T value) =>
        new(RgaOperationKind.Insert, id, parent, value);

    /// <summary>Creates a delete operation.</summary>
    /// <param name="id">The id of the element to delete.</param>
    /// <returns>The delete operation.</returns>
    public static RgaOperation<T> Delete(Dot id) =>
        new(RgaOperationKind.Delete, id, default, default);

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        writer.WriteDot(Id);
        if (Kind == RgaOperationKind.Insert)
        {
            writer.WriteDot(Parent);
            serializer.Write(ref writer, Value!);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static RgaOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var kind = (RgaOperationKind)reader.ReadByte();
        Dot id = reader.ReadDot();
        if (kind == RgaOperationKind.Insert)
        {
            Dot parent = reader.ReadDot();
            return Insert(id, parent, serializer.Read(ref reader));
        }

        return Delete(id);
    }

    [SuppressMessage("Usage", "CA2225", Justification = "Factory methods Insert/Delete are provided.")]
    internal static RgaOperation<T> ReadFrom(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        var kind = (RgaOperationKind)reader.ReadByte();
        Dot id = reader.ReadDot();
        if (kind == RgaOperationKind.Insert)
        {
            Dot parent = reader.ReadDot();
            return Insert(id, parent, serializer.Read(ref reader));
        }

        return Delete(id);
    }
}
