// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>
/// A FugueMax tree sequence CRDT for collaboratively edited ordered collections. Each
/// element is a tree node whose visible order is defined by deterministic in-order traversal.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// State is a grow-only set of nodes plus grow-only tombstones. Merging unions the node set
/// and takes the logical OR of tombstones. Operation application is idempotent and
/// order-tolerant: a node remains hidden until its ancestors are known. Mutable and not
/// thread-safe.
/// </remarks>
public sealed partial class FugueSequence<T> :
    IConvergent<FugueSequence<T>>,
    IOperationConvergent<FugueOperation<T>>,
    IGarbageCollectable,
    IEquatable<FugueSequence<T>>
{
    private static Dot Root => default;

    private readonly Dictionary<Dot, Node> _nodes;
    private readonly HashSet<Dot> _deleted;
    private readonly VersionVector _version;
    private readonly ReplicaId _replica;

    /// <summary>Initializes an empty sequence with a new random replica id.</summary>
    public FugueSequence()
        : this(ReplicaId.New())
    {
    }

    /// <summary>Initializes an empty sequence for <paramref name="replica"/>.</summary>
    /// <param name="replica">The local replica id used to stamp inserted nodes.</param>
    public FugueSequence(ReplicaId replica)
    {
        _nodes = [];
        _deleted = [];
        _version = new VersionVector();
        _replica = replica;
    }

    private FugueSequence(
        ReplicaId replica,
        Dictionary<Dot, Node> nodes,
        HashSet<Dot> deleted,
        VersionVector version)
    {
        _replica = replica;
        _nodes = nodes;
        _deleted = deleted;
        _version = version;
    }

    /// <summary>Gets the number of visible, non-deleted elements.</summary>
    public int Count => VisibleIds().Count;

    /// <summary>Gets the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based visible index.</param>
    /// <returns>The element value.</returns>
    public T this[int index] => _nodes[VisibleIdAt(index)].Value;

    /// <summary>Gets the visible elements in sequence order.</summary>
    public IReadOnlyList<T> Value => ToArray();

    /// <inheritdoc/>
    public VersionVector ObservedVersion => _version.Clone();

    /// <summary>Gets the visible elements as a string when <typeparamref name="T"/> is <see cref="char"/>.</summary>
    public string Text
    {
        get
        {
            if (ToArray() is char[] chars)
            {
                return new string(chars);
            }

            throw new InvalidOperationException("Text is only available for FugueSequence<char>.");
        }
    }

    /// <summary>Returns the visible elements in sequence order.</summary>
    /// <returns>An array of visible element values.</returns>
    public T[] ToArray()
    {
        List<Dot> ids = VisibleIds();
        var values = new T[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            values[i] = _nodes[ids[i]].Value;
        }

        return values;
    }

    /// <summary>Inserts <paramref name="value"/> at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position at which to insert (0..Count).</param>
    /// <param name="value">The value to insert.</param>
    /// <returns>The operation to broadcast.</returns>
    public FugueOperation<T> InsertAt(int index, T value)
    {
        List<Dot> ids = VisibleIds();
        if (index < 0 || index > ids.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        Dot left = index == 0 ? Root : ids[index - 1];
        Dot id = _version.Increment(_replica);
        Dot parentId;
        FugueSide side;
        if (index == ids.Count || !HasRightChild(left))
        {
            parentId = left;
            side = FugueSide.Right;
        }
        else
        {
            parentId = ids[index];
            side = FugueSide.Left;
        }

        ApplyInsert(id, parentId, side, value);
        return FugueOperation<T>.Insert(id, parentId, side, value);
    }

    /// <summary>Appends <paramref name="value"/> to the end of the sequence.</summary>
    /// <param name="value">The value to append.</param>
    /// <returns>The operation to broadcast.</returns>
    public FugueOperation<T> Append(T value) => InsertAt(Count, value);

    /// <summary>Deletes the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position to delete (0..Count-1).</param>
    /// <returns>The operation to broadcast.</returns>
    public FugueOperation<T> Delete(int index)
    {
        Dot id = VisibleIdAt(index);
        MarkDeleted(id);
        return FugueOperation<T>.Delete(id);
    }

    /// <inheritdoc/>
    public void Merge(FugueSequence<T> other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<Dot, Node> entry in other._nodes)
        {
            bool deleted = entry.Value.Deleted || _deleted.Contains(entry.Key) || other._deleted.Contains(entry.Key);
            if (deleted)
            {
                _deleted.Add(entry.Key);
            }

            if (_nodes.TryGetValue(entry.Key, out Node existing))
            {
                _nodes[entry.Key] = existing.WithDeleted(existing.Deleted || deleted);
            }
            else
            {
                _nodes[entry.Key] = entry.Value.WithDeleted(deleted);
            }
        }

        foreach (Dot dot in other._deleted)
        {
            MarkDeleted(dot);
        }

        _version.Merge(other._version);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(FugueSequence<T> other)
    {
        Throw.IfNull(other);
        CrdtOrder nodes = CompareSets(
            HasKeyNotIn(_nodes, other._nodes),
            HasKeyNotIn(other._nodes, _nodes));
        CrdtOrder deleted = CompareSets(HasNotIn(_deleted, other._deleted), HasNotIn(other._deleted, _deleted));
        return Combine(nodes, deleted);
    }

    /// <inheritdoc/>
    public FugueSequence<T> Clone() =>
        new(_replica, new Dictionary<Dot, Node>(_nodes), new HashSet<Dot>(_deleted), _version.Clone());

    /// <inheritdoc/>
    public bool Apply(FugueOperation<T> operation)
    {
        _version.Observe(operation.Id);
        if (operation.Kind == FugueOperationKind.Insert)
        {
            return ApplyInsert(operation.Id, operation.ParentId, operation.Side, operation.Value!);
        }

        return MarkDeleted(operation.Id);
    }

    /// <inheritdoc/>
    public void CollectStable(StableCut cut)
    {
        Throw.IfNull(cut);

        var removable = new HashSet<Dot>();
        foreach (Dot dot in _deleted)
        {
            if (_nodes.ContainsKey(dot) && cut.IsStable(dot))
            {
                removable.Add(dot);
            }
        }

        if (removable.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<Dot, Node> entry in _nodes)
        {
            if (!removable.Contains(entry.Key))
            {
                removable.Remove(entry.Value.ParentId);
            }
        }

        foreach (Dot dot in removable)
        {
            _nodes.Remove(dot);
            _deleted.Remove(dot);
        }
    }

    /// <summary>Serializes the sequence to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        Write(ref writer, serializer);
    }

    /// <summary>Serializes the sequence to a new byte array.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<T> serializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, serializer);
        return buffer.WrittenSpan.ToArray();
    }

    internal void Write(ref CrdtWriter writer, ICrdtValueSerializer<T> serializer)
    {
        var ids = new List<Dot>(_nodes.Keys);
        ids.Sort();
        writer.WriteReplicaId(_replica);
        writer.WriteVarUInt64((ulong)ids.Count);
        foreach (Dot id in ids)
        {
            Node node = _nodes[id];
            writer.WriteDot(id);
            writer.WriteDot(node.ParentId);
            writer.WriteByte((byte)node.Side);
            writer.WriteBool(node.Deleted);
            serializer.Write(ref writer, node.Value);
        }

        var deleted = new List<Dot>(_deleted);
        deleted.Sort();
        writer.WriteVarUInt64((ulong)deleted.Count);
        foreach (Dot dot in deleted)
        {
            writer.WriteDot(dot);
        }

        _version.Write(ref writer);
    }

    /// <summary>Decodes a sequence from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded sequence.</returns>
    public static FugueSequence<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        ReplicaId replica = reader.ReadReplicaId();
        var sequence = new FugueSequence<T>(replica);

        int nodeCount = reader.ReadCount();
        for (int i = 0; i < nodeCount; i++)
        {
            Dot id = reader.ReadDot();
            Dot parentId = reader.ReadDot();
            var side = (FugueSide)reader.ReadByte();
            bool deleted = reader.ReadBool();
            T value = serializer.Read(ref reader);
            sequence._nodes[id] = new Node(parentId, side, value, deleted);
            if (deleted)
            {
                sequence._deleted.Add(id);
            }
        }

        int deletedCount = reader.ReadCount();
        for (int i = 0; i < deletedCount; i++)
        {
            sequence.MarkDeleted(reader.ReadDot());
        }

        sequence._version.Merge(VersionVector.Read(ref reader));
        return sequence;
    }

    /// <inheritdoc/>
    public bool Equals(FugueSequence<T>? other)
    {
        if (other is null || other._nodes.Count != _nodes.Count || other._deleted.Count != _deleted.Count)
        {
            return false;
        }

        foreach (KeyValuePair<Dot, Node> entry in _nodes)
        {
            if (!other._nodes.TryGetValue(entry.Key, out Node otherNode) || !entry.Value.Equals(otherNode))
            {
                return false;
            }
        }

        return _deleted.SetEquals(other._deleted);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as FugueSequence<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_nodes.Count, _deleted.Count);

    private bool ApplyInsert(Dot id, Dot parentId, FugueSide side, T value)
    {
        if (_nodes.ContainsKey(id))
        {
            return false;
        }

        bool deleted = _deleted.Contains(id);
        _nodes[id] = new Node(parentId, side, value, deleted);
        return true;
    }

    private bool MarkDeleted(Dot id)
    {
        bool added = _deleted.Add(id);
        if (_nodes.TryGetValue(id, out Node node) && !node.Deleted)
        {
            _nodes[id] = node.WithDeleted(true);
            return true;
        }

        return added;
    }

    private Dot VisibleIdAt(int index)
    {
        List<Dot> ids = VisibleIds();
        if (index < 0 || index >= ids.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        return ids[index];
    }

    private List<Dot> VisibleIds()
    {
        Dictionary<Dot, Children> children = BuildChildren();
        var result = new List<Dot>(_nodes.Count);
        Traverse(Root, children, result);
        return result;
    }

    private void Traverse(Dot id, Dictionary<Dot, Children> children, List<Dot> result)
    {
        if (children.TryGetValue(id, out Children? childSet))
        {
            foreach (Dot child in childSet.Left)
            {
                Traverse(child, children, result);
            }
        }

        if (id != Root && _nodes.TryGetValue(id, out Node node) && !node.Deleted && !_deleted.Contains(id))
        {
            result.Add(id);
        }

        if (children.TryGetValue(id, out childSet))
        {
            foreach (Dot child in childSet.Right)
            {
                Traverse(child, children, result);
            }
        }
    }

    private bool HasRightChild(Dot id)
    {
        foreach (Node node in _nodes.Values)
        {
            if (node.ParentId == id && node.Side == FugueSide.Right)
            {
                return true;
            }
        }

        return false;
    }

    private Dictionary<Dot, Children> BuildChildren()
    {
        var map = new Dictionary<Dot, Children>();
        foreach (KeyValuePair<Dot, Node> entry in _nodes)
        {
            if (entry.Value.ParentId != Root && !_nodes.ContainsKey(entry.Value.ParentId))
            {
                continue;
            }

            if (!map.TryGetValue(entry.Value.ParentId, out Children? children))
            {
                children = new Children();
                map[entry.Value.ParentId] = children;
            }

            if (entry.Value.Side == FugueSide.Left)
            {
                children.Left.Add(entry.Key);
            }
            else
            {
                children.Right.Add(entry.Key);
            }
        }

        foreach (Children children in map.Values)
        {
            children.Left.Sort();
            children.Right.Sort();
        }

        return map;
    }

    private static bool HasKeyNotIn(Dictionary<Dot, Node> source, Dictionary<Dot, Node> other)
    {
        foreach (Dot key in source.Keys)
        {
            if (!other.ContainsKey(key))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNotIn(HashSet<Dot> source, HashSet<Dot> other)
    {
        foreach (Dot dot in source)
        {
            if (!other.Contains(dot))
            {
                return true;
            }
        }

        return false;
    }

    private static CrdtOrder CompareSets(bool thisHasExtra, bool otherHasExtra) => (thisHasExtra, otherHasExtra) switch
    {
        (true, true) => CrdtOrder.Concurrent,
        (true, false) => CrdtOrder.Greater,
        (false, true) => CrdtOrder.Less,
        _ => CrdtOrder.Equal,
    };

    private static CrdtOrder Combine(CrdtOrder left, CrdtOrder right)
    {
        if (left == CrdtOrder.Equal)
        {
            return right;
        }

        if (right == CrdtOrder.Equal)
        {
            return left;
        }

        return left == right ? left : CrdtOrder.Concurrent;
    }

    private sealed class Children
    {
        public List<Dot> Left { get; } = [];

        public List<Dot> Right { get; } = [];
    }

    private readonly struct Node : IEquatable<Node>
    {
        public Node(Dot parentId, FugueSide side, T value, bool deleted)
        {
            ParentId = parentId;
            Side = side;
            Value = value;
            Deleted = deleted;
        }

        public Dot ParentId { get; }

        public FugueSide Side { get; }

        public T Value { get; }

        public bool Deleted { get; }

        public Node WithDeleted(bool deleted) => new(ParentId, Side, Value, deleted);

        public bool Equals(Node other) =>
            ParentId == other.ParentId
            && Side == other.Side
            && Deleted == other.Deleted
            && EqualityComparer<T>.Default.Equals(Value, other.Value);

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is Node other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ParentId, Side, Deleted, Value);
    }
}
