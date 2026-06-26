// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>Identifies the kind of an <see cref="YataOperation{T}"/>.</summary>
public enum YataOperationKind
{
    /// <summary>An insertion of a new element.</summary>
    Insert,

    /// <summary>A deletion (tombstone) of an existing element.</summary>
    Delete,
}

/// <summary>
/// An operation broadcast by a <see cref="YataSequence{T}"/>. Insert operations carry the
/// element id, value, and the left/right origins captured at the insertion point; delete
/// operations carry the tombstoned element id.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct YataOperation<T>
{
    private YataOperation(YataOperationKind kind, Dot id, Dot originLeft, Dot originRight, T? value)
    {
        Kind = kind;
        Id = id;
        OriginLeft = originLeft;
        OriginRight = originRight;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public YataOperationKind Kind { get; }

    /// <summary>Gets the id of the element this operation concerns.</summary>
    public Dot Id { get; }

    /// <summary>Gets the left origin for insert operations.</summary>
    public Dot OriginLeft { get; }

    /// <summary>Gets the right origin for insert operations.</summary>
    public Dot OriginRight { get; }

    /// <summary>Gets the inserted value for insert operations; otherwise <see langword="default"/>.</summary>
    public T? Value { get; }

    /// <summary>Creates an insert operation.</summary>
    /// <param name="id">The new element's id.</param>
    /// <param name="originLeft">The id of the visible element to the left, or the start sentinel.</param>
    /// <param name="originRight">The id of the visible element to the right, or the end sentinel.</param>
    /// <param name="value">The inserted value.</param>
    /// <returns>The insert operation.</returns>
    public static YataOperation<T> Insert(Dot id, Dot originLeft, Dot originRight, T value) =>
        new(YataOperationKind.Insert, id, originLeft, originRight, value);

    /// <summary>Creates a delete operation.</summary>
    /// <param name="id">The id of the element to delete.</param>
    /// <returns>The delete operation.</returns>
    public static YataOperation<T> Delete(Dot id) =>
        new(YataOperationKind.Delete, id, default, default, default);

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        writer.WriteDot(Id);
        if (Kind == YataOperationKind.Insert)
        {
            writer.WriteDot(OriginLeft);
            writer.WriteDot(OriginRight);
            serializer.Write(ref writer, Value!);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static YataOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return ReadFrom(ref reader, serializer);
    }

    [SuppressMessage("Usage", "CA2225", Justification = "Factory methods Insert/Delete are provided.")]
    internal static YataOperation<T> ReadFrom(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        var kind = (YataOperationKind)reader.ReadByte();
        Dot id = reader.ReadDot();
        if (kind == YataOperationKind.Insert)
        {
            Dot originLeft = reader.ReadDot();
            Dot originRight = reader.ReadDot();
            return Insert(id, originLeft, originRight, serializer.Read(ref reader));
        }

        return Delete(id);
    }
}
