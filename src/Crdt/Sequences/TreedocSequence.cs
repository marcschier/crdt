// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>Identifies the kind of a <see cref="TreedocOperation{T}"/>.</summary>
public enum TreedocOperationKind
{
    /// <summary>An insertion of a new element.</summary>
    Insert,

    /// <summary>A deletion of an existing element.</summary>
    Delete,
}

/// <summary>An operation emitted by <see cref="TreedocSequence{T}"/>.</summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct TreedocOperation<T>
{
    internal TreedocOperation(TreedocOperationKind kind, TreedocPosition position, T? value)
    {
        Kind = kind;
        Position = position;
        Value = value;
    }

    /// <summary>Gets the operation kind.</summary>
    public TreedocOperationKind Kind { get; }

    /// <summary>Gets the inserted value for insert operations.</summary>
    public T? Value { get; }

    internal TreedocPosition Position { get; }

    /// <summary>Creates an insert operation.</summary>
    /// <param name="position">The element position.</param>
    /// <param name="value">The inserted value.</param>
    /// <returns>The insert operation.</returns>
    internal static TreedocOperation<T> Insert(TreedocPosition position, T value) =>
        new(TreedocOperationKind.Insert, position, value);

    /// <summary>Creates a delete operation.</summary>
    /// <param name="position">The deleted element position.</param>
    /// <returns>The delete operation.</returns>
    internal static TreedocOperation<T> Delete(TreedocPosition position) =>
        new(TreedocOperationKind.Delete, position, default);

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        writer.WriteByte((byte)Kind);
        Position.Write(ref writer);
        if (Kind == TreedocOperationKind.Insert)
        {
            serializer.Write(ref writer, Value!);
        }
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static TreedocOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var kind = (TreedocOperationKind)reader.ReadByte();
        TreedocPosition position = new TreedocStrategy().Read(ref reader);
        return kind == TreedocOperationKind.Insert
            ? Insert(position, serializer.Read(ref reader))
            : Delete(position);
    }
}

/// <summary>
/// A Treedoc sequence CRDT using tree-path position identifiers with replica tie-breakers.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class TreedocSequence<T> :
    IConvergent<TreedocSequence<T>>,
    IDeltaConvergent<TreedocSequence<T>, TreedocSequence<T>>,
    IOperationConvergent<TreedocOperation<T>>,
    IEquatable<TreedocSequence<T>>
{
    private readonly PositionalSequenceCore<T, TreedocPosition, TreedocStrategy> _core;

    /// <summary>Initializes an empty sequence.</summary>
    public TreedocSequence() => _core = new PositionalSequenceCore<T, TreedocPosition, TreedocStrategy>();

    private TreedocSequence(PositionalSequenceCore<T, TreedocPosition, TreedocStrategy> core) => _core = core;

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
    public TreedocOperation<T> Insert(ReplicaId replica, int index, T value) =>
        TreedocOperation<T>.Insert(_core.Insert(replica, index, value), value);

    /// <summary>Appends <paramref name="value"/> to the end of the sequence.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="value">The value to append.</param>
    /// <returns>The operation to broadcast.</returns>
    public TreedocOperation<T> Append(ReplicaId replica, T value) => Insert(replica, Count, value);

    /// <summary>Deletes the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position to delete.</param>
    /// <returns>The operation to broadcast.</returns>
    public TreedocOperation<T> Delete(int index) => TreedocOperation<T>.Delete(_core.Delete(index));

    /// <inheritdoc/>
    public void Merge(TreedocSequence<T> other)
    {
        Throw.IfNull(other);
        _core.Merge(other._core);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(TreedocSequence<T> other)
    {
        Throw.IfNull(other);
        return _core.Compare(other._core);
    }

    /// <inheritdoc/>
    public TreedocSequence<T> Clone() => new(_core.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out TreedocSequence<T> delta)
    {
        if (_core.TryExtractDelta(out PositionalSequenceCore<T, TreedocPosition, TreedocStrategy>? coreDelta))
        {
            delta = new TreedocSequence<T>(coreDelta);
            return true;
        }

        delta = null;
        return false;
    }

    /// <inheritdoc/>
    public void MergeDelta(TreedocSequence<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(TreedocOperation<T> operation) => operation.Kind == TreedocOperationKind.Insert
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
    public static TreedocSequence<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null) =>
        new(PositionalSequenceCore<T, TreedocPosition, TreedocStrategy>.ReadFrom(data, serializer, options));

    /// <summary>Serializes the sequence to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer) => _core.ToJson(serializer);

    /// <summary>Deserializes a sequence from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded sequence.</returns>
    public static TreedocSequence<T> FromJson(string json, ICrdtValueSerializer<T> serializer) =>
        new(PositionalSequenceCore<T, TreedocPosition, TreedocStrategy>.FromJson(json, serializer));

    /// <inheritdoc/>
    public bool Equals(TreedocSequence<T>? other) => other is not null && _core.Equals(other._core);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as TreedocSequence<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => _core.GetHashCode();
}

internal readonly struct TreedocComponent : IEquatable<TreedocComponent>
{
    public TreedocComponent(uint branch, ReplicaId site)
    {
        Branch = branch;
        Site = site;
    }

    public uint Branch { get; }

    public ReplicaId Site { get; }

    public bool Equals(TreedocComponent other) => Branch == other.Branch && Site.Equals(other.Site);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TreedocComponent other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Branch, Site);
}

internal sealed class TreedocPosition : ISequencePosition<TreedocPosition>
{
    public TreedocPosition(IReadOnlyList<TreedocComponent> path, Dot dot)
    {
        Path = [.. path];
        Dot = dot;
    }

    public IReadOnlyList<TreedocComponent> Path { get; }

    public Dot Dot { get; }

    public int CompareTo(TreedocPosition? other)
    {
        if (other is null)
        {
            return 1;
        }

        int count = Math.Min(Path.Count, other.Path.Count);
        for (int i = 0; i < count; i++)
        {
            int byBranch = Path[i].Branch.CompareTo(other.Path[i].Branch);
            if (byBranch != 0)
            {
                return byBranch;
            }

            int bySite = Path[i].Site.CompareTo(other.Path[i].Site);
            if (bySite != 0)
            {
                return bySite;
            }
        }

        int byLength = Path.Count.CompareTo(other.Path.Count);
        return byLength != 0 ? byLength : Dot.CompareTo(other.Dot);
    }

    public bool Equals(TreedocPosition? other) => other is not null && Dot.Equals(other.Dot);

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as TreedocPosition);

    public override int GetHashCode() => Dot.GetHashCode();

    public void Write(ref CrdtWriter writer)
    {
        writer.WriteVarUInt64((ulong)Path.Count);
        foreach (TreedocComponent component in Path)
        {
            writer.WriteVarUInt32(component.Branch);
            writer.WriteReplicaId(component.Site);
        }

        writer.WriteDot(Dot);
    }

    public void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("path");
        foreach (TreedocComponent component in Path)
        {
            writer.WriteStartObject();
            writer.WriteNumber("branch", component.Branch);
            writer.WriteString("site", component.Site.Value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteString("replica", Dot.Replica.Value);
        writer.WriteNumber("sequence", Dot.Sequence);
        writer.WriteEndObject();
    }
}

internal readonly struct TreedocStrategy : ISequencePositionStrategy<TreedocPosition>
{
    private const uint Base = 65536;

    public TreedocPosition Allocate(TreedocPosition? left, TreedocPosition? right, Dot dot)
    {
        var path = new List<TreedocComponent>();
        int depth = 0;
        while (true)
        {
            uint leftBranch = BranchOr(left, depth, 0U);
            uint rightBranch = BranchOr(right, depth, Base - 1U);
            if (rightBranch > leftBranch + 1U)
            {
                path.Add(new TreedocComponent(leftBranch + 1U, dot.Replica));
                return new TreedocPosition(path, dot);
            }

            path.Add(ComponentOr(left, depth, new TreedocComponent(leftBranch, dot.Replica)));
            depth++;
        }
    }

    public TreedocPosition Read(ref CrdtReader reader)
    {
        int count = reader.ReadCount();
        var path = new List<TreedocComponent>(count);
        for (int i = 0; i < count; i++)
        {
            path.Add(new TreedocComponent(reader.ReadVarUInt32(), reader.ReadReplicaId()));
        }

        return new TreedocPosition(path, reader.ReadDot());
    }

    public TreedocPosition ReadJson(JsonElement element)
    {
        var path = new List<TreedocComponent>();
        foreach (JsonElement item in element.GetProperty("path").EnumerateArray())
        {
            var site = new ReplicaId(item.GetProperty("site").GetGuid());
            path.Add(new TreedocComponent(item.GetProperty("branch").GetUInt32(), site));
        }

        var replica = new ReplicaId(element.GetProperty("replica").GetGuid());
        return new TreedocPosition(path, new Dot(replica, element.GetProperty("sequence").GetUInt64()));
    }

    private static uint BranchOr(TreedocPosition? position, int depth, uint fallback) =>
        position is not null && depth < position.Path.Count ? position.Path[depth].Branch : fallback;

    private static TreedocComponent ComponentOr(TreedocPosition? position, int depth, TreedocComponent fallback) =>
        position is not null && depth < position.Path.Count ? position.Path[depth] : fallback;
}
