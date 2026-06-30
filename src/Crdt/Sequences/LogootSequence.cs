// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>Identifies the kind of a <see cref="LogootOperation{T}"/>.</summary>
public enum LogootOperationKind
{
    /// <summary>An insertion of a new element.</summary>
    Insert,

    /// <summary>A deletion of an existing element.</summary>
    Delete,
}

/// <summary>An operation emitted by <see cref="LogootSequence{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct LogootOperation<T>
{
    internal LogootOperation(LogootOperationKind kind, LogootPosition position, T? value)
    {
        Kind = kind;
        Position = position;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public LogootOperationKind Kind { get; }

    /// <summary>Gets the inserted value for insert operations.</summary>
    public T? Value { get; }

    internal LogootPosition Position { get; }

    /// <summary>Creates an insert operation.</summary>
    /// <param name="position">The element position.</param>
    /// <param name="value">The inserted value.</param>
    /// <returns>The insert operation.</returns>
    internal static LogootOperation<T> Insert(LogootPosition position, T value) =>
        new(LogootOperationKind.Insert, position, value);

    /// <summary>Creates a delete operation.</summary>
    /// <param name="position">The deleted element position.</param>
    /// <returns>The delete operation.</returns>
    internal static LogootOperation<T> Delete(LogootPosition position) =>
        new(LogootOperationKind.Delete, position, default);

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        Position.Write(ref writer);
        if (Kind == LogootOperationKind.Insert)
        {
            serializer.Write(ref writer, Value!);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static LogootOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var kind = (LogootOperationKind)reader.ReadByte();
        LogootPosition position = new LogootStrategy().Read(ref reader);
        return kind == LogootOperationKind.Insert
            ? Insert(position, serializer.Read(ref reader))
            : Delete(position);
    }
}

/// <summary>
/// A Logoot sequence CRDT using lexicographic position identifiers with replica tie-breakers.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class LogootSequence<T> :
    IConvergent<LogootSequence<T>>,
    IDeltaConvergent<LogootSequence<T>, LogootSequence<T>>,
    IOperationConvergent<LogootOperation<T>>,
    IGarbageCollectable,
    IEquatable<LogootSequence<T>>
{
    private readonly PositionalSequenceCore<T, LogootPosition, LogootStrategy> _core;

    /// <summary>Initializes an empty sequence.</summary>
    public LogootSequence() => _core = new PositionalSequenceCore<T, LogootPosition, LogootStrategy>();

    private LogootSequence(PositionalSequenceCore<T, LogootPosition, LogootStrategy> core) => _core = core;

    /// <summary>Gets the number of visible elements.</summary>
    public int Count => _core.Count;

    /// <inheritdoc/>
    public VersionVector ObservedVersion => _core.ObservedVersion;

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
    public LogootOperation<T> Insert(ReplicaId replica, int index, T value) =>
        LogootOperation<T>.Insert(_core.Insert(replica, index, value), value);

    /// <summary>Appends <paramref name="value"/> to the end of the sequence.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="value">The value to append.</param>
    /// <returns>The operation to broadcast.</returns>
    public LogootOperation<T> Append(ReplicaId replica, T value) => Insert(replica, Count, value);

    /// <summary>Deletes the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position to delete.</param>
    /// <returns>The operation to broadcast.</returns>
    public LogootOperation<T> Delete(int index) => LogootOperation<T>.Delete(_core.Delete(index));

    /// <inheritdoc/>
    public void Merge(LogootSequence<T> other)
    {
        Throw.IfNull(other);
        _core.Merge(other._core);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(LogootSequence<T> other)
    {
        Throw.IfNull(other);
        return _core.Compare(other._core);
    }

    /// <inheritdoc/>
    public LogootSequence<T> Clone() => new(_core.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out LogootSequence<T> delta)
    {
        if (_core.TryExtractDelta(out PositionalSequenceCore<T, LogootPosition, LogootStrategy>? coreDelta))
        {
            delta = new LogootSequence<T>(coreDelta);
            return true;
        }

        delta = null;
        return false;
    }

    /// <inheritdoc/>
    public void MergeDelta(LogootSequence<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(LogootOperation<T> operation) => operation.Kind == LogootOperationKind.Insert
        ? _core.ApplyInsert(operation.Position, operation.Value!)
        : _core.ApplyDelete(operation.Position);

    /// <inheritdoc/>
    public void CollectStable(StableCut cut) => _core.CollectStable(cut);

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
    public static LogootSequence<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null) =>
        new(PositionalSequenceCore<T, LogootPosition, LogootStrategy>.ReadFrom(data, serializer, options));

    /// <summary>Serializes the sequence to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer) => _core.ToJson(serializer);

    /// <summary>Deserializes a sequence from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded sequence.</returns>
    public static LogootSequence<T> FromJson(string json, ICrdtValueSerializer<T> serializer) =>
        new(PositionalSequenceCore<T, LogootPosition, LogootStrategy>.FromJson(json, serializer));

    /// <inheritdoc/>
    public bool Equals(LogootSequence<T>? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as LogootSequence<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => _core.GetHashCode();
}

internal readonly struct LogootSegment : IEquatable<LogootSegment>
{
    public LogootSegment(uint digit, ReplicaId site)
    {
        Digit = digit;
        Site = site;
    }

    public uint Digit { get; }

    public ReplicaId Site { get; }

    public bool Equals(LogootSegment other) => Digit == other.Digit && Site.Equals(other.Site);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is LogootSegment other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Digit, Site);
}

internal sealed class LogootPosition : ISequencePosition<LogootPosition>
{
    public LogootPosition(IReadOnlyList<LogootSegment> segments, Dot dot)
    {
        Segments = [.. segments];
        Dot = dot;
    }

    public IReadOnlyList<LogootSegment> Segments { get; }

    public Dot Dot { get; }

    public int CompareTo(LogootPosition? other)
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

    public bool Equals(LogootPosition? other) => other is not null && Dot.Equals(other.Dot);

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as LogootPosition);

    public override int GetHashCode() => Dot.GetHashCode();

    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)Segments.Count);
        foreach (LogootSegment segment in Segments)
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
        foreach (LogootSegment segment in Segments)
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

internal readonly struct LogootStrategy : ISequencePositionStrategy<LogootPosition>
{
    private const uint Base = 65536;

    public LogootPosition Allocate(LogootPosition? left, LogootPosition? right, Dot dot)
    {
        var segments = new List<LogootSegment>();
        int depth = 0;
        while (true)
        {
            uint leftDigit = DigitOr(left, depth, 0U);
            uint rightDigit = DigitOr(right, depth, Base - 1U);
            if (rightDigit > leftDigit + 1U)
            {
                segments.Add(new LogootSegment(leftDigit + 1U, dot.Replica));
                return new LogootPosition(segments, dot);
            }

            segments.Add(SegmentOr(left, depth, new LogootSegment(leftDigit, dot.Replica)));
            depth++;
        }
    }

    public LogootPosition Read(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var segments = new List<LogootSegment>(count);
        for (int i = 0; i < count; i++)
        {
            segments.Add(new LogootSegment(reader.ReadVarUInt32(), reader.ReadReplicaId()));
        }

        return new LogootPosition(segments, reader.ReadDot());
    }

    public LogootPosition ReadJson(JsonElement element)
    {
        var segments = new List<LogootSegment>();
        foreach (JsonElement item in element.GetProperty("segments").EnumerateArray())
        {
            var site = new ReplicaId(item.GetProperty("site").GetGuid());
            segments.Add(new LogootSegment(item.GetProperty("digit").GetUInt32(), site));
        }

        var replica = new ReplicaId(element.GetProperty("replica").GetGuid());
        return new LogootPosition(segments, new Dot(replica, element.GetProperty("sequence").GetUInt64()));
    }

    private static uint DigitOr(LogootPosition? position, int depth, uint fallback) =>
        position is not null && depth < position.Segments.Count ? position.Segments[depth].Digit : fallback;

    private static LogootSegment SegmentOr(LogootPosition? position, int depth, LogootSegment fallback) =>
        position is not null && depth < position.Segments.Count ? position.Segments[depth] : fallback;
}
