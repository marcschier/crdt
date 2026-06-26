// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>Identifies the kind of a <see cref="LSeqOperation{T}"/>.</summary>
public enum LSeqOperationKind
{
    /// <summary>An insertion of a new element.</summary>
    Insert,

    /// <summary>A deletion of an existing element.</summary>
    Delete,
}

/// <summary>An operation emitted by <see cref="LSeqSequence{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct LSeqOperation<T>
{
    internal LSeqOperation(LSeqOperationKind kind, LSeqPosition position, T? value)
    {
        Kind = kind;
        Position = position;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public LSeqOperationKind Kind { get; }

    /// <summary>Gets the inserted value for insert operations.</summary>
    public T? Value { get; }

    internal LSeqPosition Position { get; }

    /// <summary>Creates an insert operation.</summary>
    /// <param name="position">The element position.</param>
    /// <param name="value">The inserted value.</param>
    /// <returns>The insert operation.</returns>
    internal static LSeqOperation<T> Insert(LSeqPosition position, T value) =>
        new(LSeqOperationKind.Insert, position, value);

    /// <summary>Creates a delete operation.</summary>
    /// <param name="position">The deleted element position.</param>
    /// <returns>The delete operation.</returns>
    internal static LSeqOperation<T> Delete(LSeqPosition position) =>
        new(LSeqOperationKind.Delete, position, default);

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        Position.Write(ref writer);
        if (Kind == LSeqOperationKind.Insert)
        {
            serializer.Write(ref writer, Value!);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static LSeqOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var kind = (LSeqOperationKind)reader.ReadByte();
        LSeqPosition position = new LSeqStrategy().Read(ref reader);
        return kind == LSeqOperationKind.Insert
            ? Insert(position, serializer.Read(ref reader))
            : Delete(position);
    }
}

/// <summary>
/// An LSEQ sequence CRDT using alternating boundary position allocation.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class LSeqSequence<T> :
    IConvergent<LSeqSequence<T>>,
    IDeltaConvergent<LSeqSequence<T>, LSeqSequence<T>>,
    IOperationConvergent<LSeqOperation<T>>,
    IEquatable<LSeqSequence<T>>
{
    private readonly PositionalSequenceCore<T, LSeqPosition, LSeqStrategy> _core;

    /// <summary>Initializes an empty sequence.</summary>
    public LSeqSequence() => _core = new PositionalSequenceCore<T, LSeqPosition, LSeqStrategy>();

    private LSeqSequence(PositionalSequenceCore<T, LSeqPosition, LSeqStrategy> core) => _core = core;

    /// <summary>Gets the number of visible elements.</summary>
    public int Count => _core.Count;

    /// <summary>Gets the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based visible index.</param>
    public T this[int index] => _core[index];

    /// <summary>Returns the visible elements in sequence order.</summary>
    /// <returns>An array of visible values.</returns>
    public T[] ToArray() => _core.ToArray();

    /// <summary>Inserts <paramref name="value"/> at <paramref name="index"/>.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="index">The visible insertion index.</param>
    /// <param name="value">The value to insert.</param>
    /// <returns>The operation to broadcast.</returns>
    public LSeqOperation<T> Insert(ReplicaId replica, int index, T value) =>
        LSeqOperation<T>.Insert(_core.Insert(replica, index, value), value);

    /// <summary>Appends <paramref name="value"/> to the end of the sequence.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="value">The value to append.</param>
    /// <returns>The operation to broadcast.</returns>
    public LSeqOperation<T> Append(ReplicaId replica, T value) => Insert(replica, Count, value);

    /// <summary>Deletes the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position to delete.</param>
    /// <returns>The operation to broadcast.</returns>
    public LSeqOperation<T> Delete(int index) => LSeqOperation<T>.Delete(_core.Delete(index));

    /// <inheritdoc/>
    public void Merge(LSeqSequence<T> other)
    {
        Throw.IfNull(other);
        _core.Merge(other._core);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(LSeqSequence<T> other)
    {
        Throw.IfNull(other);
        return _core.Compare(other._core);
    }

    /// <inheritdoc/>
    public LSeqSequence<T> Clone() => new(_core.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out LSeqSequence<T> delta)
    {
        if (_core.TryExtractDelta(out PositionalSequenceCore<T, LSeqPosition, LSeqStrategy>? coreDelta))
        {
            delta = new LSeqSequence<T>(coreDelta);
            return true;
        }

        delta = null;
        return false;
    }

    /// <inheritdoc/>
    public void MergeDelta(LSeqSequence<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(LSeqOperation<T> operation) => operation.Kind == LSeqOperationKind.Insert
        ? _core.ApplyInsert(operation.Position, operation.Value!)
        : _core.ApplyDelete(operation.Position);

    /// <summary>Serializes the sequence to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer) =>
        _core.WriteTo(output, serializer);

    /// <summary>Serializes the sequence to a new byte array.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<T> serializer) => _core.ToByteArray(serializer);

    /// <summary>Decodes a sequence from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded sequence.</returns>
    public static LSeqSequence<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null) =>
        new(PositionalSequenceCore<T, LSeqPosition, LSeqStrategy>.ReadFrom(data, serializer, options));

    /// <summary>Serializes the sequence to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer) => _core.ToJson(serializer);

    /// <summary>Deserializes a sequence from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded sequence.</returns>
    public static LSeqSequence<T> FromJson(string json, ICrdtValueSerializer<T> serializer) =>
        new(PositionalSequenceCore<T, LSeqPosition, LSeqStrategy>.FromJson(json, serializer));

    /// <inheritdoc/>
    public bool Equals(LSeqSequence<T>? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as LSeqSequence<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => _core.GetHashCode();
}

internal readonly struct LSeqSegment : IEquatable<LSeqSegment>
{
    public LSeqSegment(uint digit, ReplicaId site)
    {
        Digit = digit;
        Site = site;
    }

    public uint Digit { get; }

    public ReplicaId Site { get; }

    public bool Equals(LSeqSegment other) => Digit == other.Digit && Site.Equals(other.Site);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LSeqSegment other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Digit, Site);
}

internal sealed class LSeqPosition : ISequencePosition<LSeqPosition>
{
    public LSeqPosition(IReadOnlyList<LSeqSegment> segments, Dot dot)
    {
        Segments = [.. segments];
        Dot = dot;
    }

    public IReadOnlyList<LSeqSegment> Segments { get; }

    public Dot Dot { get; }

    public int CompareTo(LSeqPosition? other)
    {
        if (other is null)
        {
            return 1;
        }

        int count = Math.Min(Segments.Count, other.Segments.Count);
        for (int i = 0; i < count; i++)
        {
            int byDigit = Segments[i].Digit.CompareTo(other.Segments[i].Digit);
            if (byDigit != 0)
            {
                return byDigit;
            }

            int bySite = Segments[i].Site.CompareTo(other.Segments[i].Site);
            if (bySite != 0)
            {
                return bySite;
            }
        }

        int byLength = Segments.Count.CompareTo(other.Segments.Count);
        return byLength != 0 ? byLength : Dot.CompareTo(other.Dot);
    }

    public bool Equals(LSeqPosition? other) => other is not null && Dot.Equals(other.Dot);

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as LSeqPosition);

    public override int GetHashCode() => Dot.GetHashCode();

    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)Segments.Count);
        foreach (LSeqSegment segment in Segments)
        {
            writer.WriteVarUInt32(segment.Digit);
            writer.WriteReplicaId(segment.Site);
        }

        writer.WriteDot(Dot);
    }

    public void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("segments");
        foreach (LSeqSegment segment in Segments)
        {
            writer.WriteStartObject();
            writer.WriteNumber("digit", segment.Digit);
            writer.WriteString("site", segment.Site.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("replica", Dot.Replica.Value);
        writer.WriteNumber("sequence", Dot.Sequence);
        writer.WriteEndObject();
    }
}

internal readonly struct LSeqStrategy : ISequencePositionStrategy<LSeqPosition>
{
    private const int InitialBaseBits = 8;

    public LSeqPosition Allocate(LSeqPosition? left, LSeqPosition? right, Dot dot)
    {
        var segments = new List<LSeqSegment>();
        int depth = 0;
        while (true)
        {
            uint numberBase = BaseFor(depth);
            uint leftDigit = DigitOr(left, depth, 0U);
            uint rightDigit = DigitOr(right, depth, numberBase - 1U);
            if (rightDigit > leftDigit + 1U)
            {
                uint digit = ChooseDigit(leftDigit, rightDigit, depth);
                segments.Add(new LSeqSegment(digit, dot.Replica));
                return new LSeqPosition(segments, dot);
            }

            segments.Add(SegmentOr(left, depth, new LSeqSegment(leftDigit, dot.Replica)));
            depth++;
        }
    }

    public LSeqPosition Read(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var segments = new List<LSeqSegment>(count);
        for (int i = 0; i < count; i++)
        {
            segments.Add(new LSeqSegment(reader.ReadVarUInt32(), reader.ReadReplicaId()));
        }

        return new LSeqPosition(segments, reader.ReadDot());
    }

    public LSeqPosition ReadJson(JsonElement element)
    {
        var segments = new List<LSeqSegment>();
        foreach (JsonElement item in element.GetProperty("segments").EnumerateArray())
        {
            var site = new ReplicaId(item.GetProperty("site").GetGuid());
            segments.Add(new LSeqSegment(item.GetProperty("digit").GetUInt32(), site));
        }

        var replica = new ReplicaId(element.GetProperty("replica").GetGuid());
        return new LSeqPosition(segments, new Dot(replica, element.GetProperty("sequence").GetUInt64()));
    }

    private static uint ChooseDigit(uint leftDigit, uint rightDigit, int depth)
    {
        uint gap = rightDigit - leftDigit - 1U;
        uint step = Math.Min(gap, 8U);
        return (depth & 1) == 0 ? leftDigit + step : rightDigit - step;
    }

    private static uint BaseFor(int depth)
    {
        int bits = Math.Min(30, InitialBaseBits + depth);
        return 1U << bits;
    }

    private static uint DigitOr(LSeqPosition? position, int depth, uint fallback) =>
        position is not null && depth < position.Segments.Count ? position.Segments[depth].Digit : fallback;

    private static LSeqSegment SegmentOr(LSeqPosition? position, int depth, LSeqSegment fallback) =>
        position is not null && depth < position.Segments.Count ? position.Segments[depth] : fallback;
}
