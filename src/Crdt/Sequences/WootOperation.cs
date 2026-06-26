// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>Identifies the kind of a <see cref="WootOperation{T}"/>.</summary>
public enum WootOperationKind
{
    /// <summary>An insertion of a new W-character.</summary>
    Insert,

    /// <summary>A deletion that makes a W-character invisible.</summary>
    Delete,
}

/// <summary>
/// An operation broadcast by a <see cref="WootSequence{T}"/>. Insert operations carry the
/// element id, value, and previous/next structural neighbours; delete operations carry the
/// tombstoned element id.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct WootOperation<T>
{
    private WootOperation(WootOperationKind kind, Dot id, Dot prevId, Dot nextId, T? value)
    {
        Kind = kind;
        Id = id;
        PrevId = prevId;
        NextId = nextId;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public WootOperationKind Kind { get; }

    /// <summary>Gets the id of the element this operation concerns.</summary>
    public Dot Id { get; }

    /// <summary>Gets the previous neighbour for insert operations.</summary>
    public Dot PrevId { get; }

    /// <summary>Gets the next neighbour for insert operations.</summary>
    public Dot NextId { get; }

    /// <summary>Gets the inserted value for insert operations; otherwise <see langword="default"/>.</summary>
    public T? Value { get; }

    /// <summary>Creates an insert operation.</summary>
    /// <param name="id">The new element's id.</param>
    /// <param name="prevId">The previous structural neighbour id, or the begin sentinel.</param>
    /// <param name="nextId">The next structural neighbour id, or the end sentinel.</param>
    /// <param name="value">The inserted value.</param>
    /// <returns>The insert operation.</returns>
    public static WootOperation<T> Insert(Dot id, Dot prevId, Dot nextId, T value) =>
        new(WootOperationKind.Insert, id, prevId, nextId, value);

    /// <summary>Creates a delete operation.</summary>
    /// <param name="id">The id of the element to delete.</param>
    /// <returns>The delete operation.</returns>
    public static WootOperation<T> Delete(Dot id) =>
        new(WootOperationKind.Delete, id, default, default, default);

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        writer.WriteDot(Id);
        if (Kind == WootOperationKind.Insert)
        {
            writer.WriteDot(PrevId);
            writer.WriteDot(NextId);
            serializer.Write(ref writer, Value!);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static WootOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return ReadFrom(ref reader, serializer);
    }

    [SuppressMessage("Usage", "CA2225", Justification = "Factory methods Insert/Delete are provided.")]
    internal static WootOperation<T> ReadFrom(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        var kind = (WootOperationKind)reader.ReadByte();
        Dot id = reader.ReadDot();
        if (kind == WootOperationKind.Insert)
        {
            Dot prevId = reader.ReadDot();
            Dot nextId = reader.ReadDot();
            return Insert(id, prevId, nextId, serializer.Read(ref reader));
        }

        return Delete(id);
    }
}
