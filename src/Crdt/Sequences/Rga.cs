// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A Replicated Growable Array (RGA): a sequence/list CRDT for collaborative ordered
/// collections. Each element has a unique <see cref="Dot"/> identity and references the
/// element it was inserted after; concurrent insertions at the same position are ordered
/// deterministically by identity, so all replicas converge to the same sequence.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// <para>
/// State is a grow-only set of nodes plus a grow-only set of tombstones, forming a
/// join-semilattice: <see cref="Merge"/> is the union of both. Operation application is
/// idempotent and order-tolerant — an element only becomes visible once its ancestors are
/// present — so operations may be delivered out of order.
/// </para>
/// <para>Mutable and not thread-safe.</para>
/// </remarks>
public sealed class Rga<T> :
    IConvergent<Rga<T>>,
    IDeltaConvergent<Rga<T>, Rga<T>>,
    IOperationConvergent<RgaOperation<T>>,
    IEquatable<Rga<T>>
{
    private static Dot Root => default;

    private readonly Dictionary<Dot, Node> _nodes;
    private readonly HashSet<Dot> _deleted;
    private readonly VersionVector _version;
    private Rga<T>? _delta;

    /// <summary>Initializes an empty sequence.</summary>
    public Rga()
    {
        _nodes = [];
        _deleted = [];
        _version = new VersionVector();
    }

    private Rga(Dictionary<Dot, Node> nodes, HashSet<Dot> deleted, VersionVector version)
    {
        _nodes = nodes;
        _deleted = deleted;
        _version = version;
    }

    /// <summary>Gets the number of visible (non-deleted, root-reachable) elements.</summary>
    public int Count => VisibleIds().Count;

    /// <summary>Gets the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based visible index.</param>
    /// <returns>The element value.</returns>
    public T this[int index] => _nodes[VisibleIdAt(index)].Value;

    /// <summary>Returns the visible elements in sequence order.</summary>
    /// <returns>An array of the visible element values.</returns>
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

    /// <summary>Inserts <paramref name="value"/> at <paramref name="index"/> on behalf of a replica.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="index">The visible position at which to insert (0..Count).</param>
    /// <param name="value">The value to insert.</param>
    /// <returns>The operation to broadcast.</returns>
    public RgaOperation<T> Insert(ReplicaId replica, int index, T value)
    {
        List<Dot> ids = VisibleIds();
        if (index < 0 || index > ids.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        Dot parent = index == 0 ? Root : ids[index - 1];
        Dot id = _version.Increment(replica);
        _nodes[id] = new Node(parent, value);
        RecordDelta().ApplyInsert(id, parent, value);
        return RgaOperation<T>.Insert(id, parent, value);
    }

    /// <summary>Appends <paramref name="value"/> to the end of the sequence.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="value">The value to append.</param>
    /// <returns>The operation to broadcast.</returns>
    public RgaOperation<T> Append(ReplicaId replica, T value) => Insert(replica, Count, value);

    /// <summary>Deletes the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position to delete (0..Count-1).</param>
    /// <returns>The operation to broadcast.</returns>
    public RgaOperation<T> Delete(int index)
    {
        List<Dot> ids = VisibleIds();
        if (index < 0 || index >= ids.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        Dot id = ids[index];
        _deleted.Add(id);
        RecordDelta()._deleted.Add(id);
        return RgaOperation<T>.Delete(id);
    }

    /// <inheritdoc/>
    public void Merge(Rga<T> other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<Dot, Node> entry in other._nodes)
        {
            _nodes.TryAdd(entry.Key, entry.Value);
        }

        foreach (Dot dot in other._deleted)
        {
            _deleted.Add(dot);
        }

        _version.Merge(other._version);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(Rga<T> other)
    {
        Throw.IfNull(other);
        CrdtOrder nodes = CompareSets(HasKeyNotIn(_nodes, other._nodes), HasKeyNotIn(other._nodes, _nodes));
        CrdtOrder deleted = CompareSets(HasNotIn(_deleted, other._deleted), HasNotIn(other._deleted, _deleted));
        return Combine(nodes, deleted);
    }

    /// <inheritdoc/>
    public Rga<T> Clone() =>
        new(new Dictionary<Dot, Node>(_nodes), new HashSet<Dot>(_deleted), _version.Clone());

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out Rga<T> delta)
    {
        if (_delta is null)
        {
            delta = null;
            return false;
        }

        delta = _delta;
        _delta = null;
        return true;
    }

    /// <inheritdoc/>
    public void MergeDelta(Rga<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(RgaOperation<T> operation)
    {
        if (operation.Kind == RgaOperationKind.Insert)
        {
            _version.Observe(operation.Id);
            return ApplyInsert(operation.Id, operation.Parent, operation.Value!);
        }

        _version.Observe(operation.Id);
        return _deleted.Add(operation.Id);
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
        var nodes = new List<Dot>(_nodes.Keys);
        nodes.Sort();
        writer.WriteVarUInt64((ulong)nodes.Count);
        foreach (Dot id in nodes)
        {
            Node node = _nodes[id];
            writer.WriteDot(id);
            writer.WriteDot(node.Parent);
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
    public static Rga<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var rga = new Rga<T>();

        int nodeCount = reader.ReadCount();
        for (int i = 0; i < nodeCount; i++)
        {
            Dot id = reader.ReadDot();
            Dot parent = reader.ReadDot();
            T value = serializer.Read(ref reader);
            rga._nodes[id] = new Node(parent, value);
        }

        int deletedCount = reader.ReadCount();
        for (int i = 0; i < deletedCount; i++)
        {
            rga._deleted.Add(reader.ReadDot());
        }

        rga._version.Merge(VersionVector.Read(ref reader));
        return rga;
    }

    /// <summary>Serializes the sequence to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var nodes = new List<Dot>(_nodes.Keys);
        nodes.Sort();
        var deleted = new List<Dot>(_deleted);
        deleted.Sort();

        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("nodes");
            foreach (Dot id in nodes)
            {
                Node node = _nodes[id];
                writer.WriteStartObject();
                writer.WriteString("id", DotText(id));
                writer.WriteString("parent", DotText(node.Parent));
                writer.WritePropertyName("value");
                serializer.WriteJson(writer, node.Value);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("deleted");
            foreach (Dot dot in deleted)
            {
                writer.WriteStringValue(DotText(dot));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a sequence from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The decoded sequence.</returns>
    public static Rga<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var rga = new Rga<T>();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        foreach (JsonElement node in root.GetProperty("nodes").EnumerateArray())
        {
            Dot id = ParseDot(node.GetProperty("id").GetString()!);
            Dot parent = ParseDot(node.GetProperty("parent").GetString()!);
            JsonElement valueElement = node.GetProperty("value");
            var reader = new Utf8JsonReader(GetRawValueBytes(valueElement));
            reader.Read();
            T value = serializer.ReadJson(ref reader);
            rga._nodes[id] = new Node(parent, value);
            rga._version.Observe(id);
        }

        foreach (JsonElement dot in root.GetProperty("deleted").EnumerateArray())
        {
            rga._deleted.Add(ParseDot(dot.GetString()!));
        }

        return rga;
    }

    /// <inheritdoc/>
    public bool Equals(Rga<T>? other)
    {
        if (other is null || other._nodes.Count != _nodes.Count || other._deleted.Count != _deleted.Count)
        {
            return false;
        }

        foreach (Dot id in _nodes.Keys)
        {
            if (!other._nodes.ContainsKey(id))
            {
                return false;
            }
        }

        return _deleted.SetEquals(other._deleted);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as Rga<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_nodes.Count, _deleted.Count);

    private bool ApplyInsert(Dot id, Dot parent, T value)
    {
        if (_nodes.ContainsKey(id))
        {
            return false;
        }

        _nodes[id] = new Node(parent, value);
        return true;
    }

    private Rga<T> RecordDelta() => _delta ??= new Rga<T>();

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
        Dictionary<Dot, List<Dot>> children = BuildChildren();
        var result = new List<Dot>(_nodes.Count);
        var stack = new Stack<Dot>();
        PushChildren(stack, children, Root);
        while (stack.Count > 0)
        {
            Dot id = stack.Pop();
            if (!_deleted.Contains(id))
            {
                result.Add(id);
            }

            PushChildren(stack, children, id);
        }

        return result;
    }

    private static void PushChildren(Stack<Dot> stack, Dictionary<Dot, List<Dot>> children, Dot parent)
    {
        if (!children.TryGetValue(parent, out List<Dot>? list))
        {
            return;
        }

        // Children are sorted ascending; pushing ascending makes the largest pop first,
        // so siblings are visited in descending-identity order.
        foreach (Dot child in list)
        {
            stack.Push(child);
        }
    }

    private Dictionary<Dot, List<Dot>> BuildChildren()
    {
        var map = new Dictionary<Dot, List<Dot>>();
        foreach (KeyValuePair<Dot, Node> entry in _nodes)
        {
            if (!map.TryGetValue(entry.Value.Parent, out List<Dot>? list))
            {
                list = [];
                map[entry.Value.Parent] = list;
            }

            list.Add(entry.Key);
        }

        foreach (List<Dot> list in map.Values)
        {
            list.Sort();
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

    private static string DotText(Dot dot) => $"{dot.Replica.Value:N}:{dot.Sequence}";

    private static Dot ParseDot(string text)
    {
        int separator = text.LastIndexOf(':');
        var replica = new ReplicaId(Guid.ParseExact(text.AsSpan(0, separator), "N"));
        ulong sequence = ulong.Parse(
            text.AsSpan(separator + 1),
            provider: System.Globalization.CultureInfo.InvariantCulture);
        return new Dot(replica, sequence);
    }

    private static byte[] GetRawValueBytes(JsonElement element)
    {
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            element.WriteTo(writer);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private readonly struct Node
    {
        public Node(Dot parent, T value)
        {
            Parent = parent;
            Value = value;
        }

        public Dot Parent { get; }

        public T Value { get; }
    }
}
