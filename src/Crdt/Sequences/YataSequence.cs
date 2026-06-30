// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A YATA/Yjs-style sequence CRDT for collaboratively edited ordered collections. Elements
/// are uniquely identified by <see cref="Dot"/> values and remember the left and right
/// visible neighbours that framed their insertion, allowing replicas to integrate concurrent
/// inserts deterministically.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// State is a grow-only set of elements plus grow-only tombstones. Merging takes the union of
/// element ids and the logical OR of tombstones, so the state forms a join-semilattice.
/// Operation application is idempotent and order-tolerant: an inserted element remains
/// structurally present but hidden from the linear order until both non-sentinel origins are
/// known. Mutable and not thread-safe.
/// </remarks>
public sealed class YataSequence<T> :
    IConvergent<YataSequence<T>>,
    IDeltaConvergent<YataSequence<T>, YataSequence<T>>,
    IOperationConvergent<YataOperation<T>>,
    IGarbageCollectable,
    IEquatable<YataSequence<T>>
{
    private static Dot Start => default;

    private readonly Dictionary<Dot, Element> _elements;
    private readonly HashSet<Dot> _deleted;
    private readonly VersionVector _version;
    private YataSequence<T>? _delta;

    /// <summary>Initializes an empty sequence.</summary>
    public YataSequence()
    {
        _elements = [];
        _deleted = [];
        _version = new VersionVector();
    }

    private YataSequence(Dictionary<Dot, Element> elements, HashSet<Dot> deleted, VersionVector version)
    {
        _elements = elements;
        _deleted = deleted;
        _version = version;
    }

    /// <summary>Gets the number of visible, non-deleted elements.</summary>
    public int Count => VisibleIds().Count;

    /// <summary>Gets the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based visible index.</param>
    /// <returns>The element value.</returns>
    public T this[int index] => _elements[VisibleIdAt(index)].Value;

    /// <summary>Returns the visible elements in sequence order.</summary>
    /// <returns>An array of visible element values.</returns>
    public T[] ToArray()
    {
        List<Dot> ids = VisibleIds();
        var values = new T[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            values[i] = _elements[ids[i]].Value;
        }

        return values;
    }

    /// <inheritdoc/>
    public VersionVector ObservedVersion => _version.Clone();

    /// <summary>Inserts <paramref name="value"/> at <paramref name="index"/> on behalf of a replica.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="index">The visible position at which to insert (0..Count).</param>
    /// <param name="value">The value to insert.</param>
    /// <returns>The operation to broadcast.</returns>
    public YataOperation<T> Insert(ReplicaId replica, int index, T value)
    {
        List<Dot> ids = VisibleIds();
        if (index < 0 || index > ids.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        Dot originLeft = index == 0 ? Start : ids[index - 1];
        Dot originRight = index == ids.Count ? default : ids[index];
        Dot id = _version.Increment(replica);
        ApplyInsert(id, originLeft, originRight, value);
        RecordDelta().ApplyInsert(id, originLeft, originRight, value);
        return YataOperation<T>.Insert(id, originLeft, originRight, value);
    }

    /// <summary>Appends <paramref name="value"/> to the end of the sequence.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="value">The value to append.</param>
    /// <returns>The operation to broadcast.</returns>
    public YataOperation<T> Append(ReplicaId replica, T value) => Insert(replica, Count, value);

    /// <summary>Deletes the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position to delete (0..Count-1).</param>
    /// <returns>The operation to broadcast.</returns>
    public YataOperation<T> Delete(int index)
    {
        Dot id = VisibleIdAt(index);
        MarkDeleted(id);
        RecordDelta().MarkDeleted(id);
        return YataOperation<T>.Delete(id);
    }

    /// <inheritdoc/>
    public void Merge(YataSequence<T> other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<Dot, Element> entry in other._elements)
        {
            bool deleted = entry.Value.Deleted || _deleted.Contains(entry.Key) || other._deleted.Contains(entry.Key);
            if (deleted)
            {
                _deleted.Add(entry.Key);
            }

            if (_elements.TryGetValue(entry.Key, out Element existing))
            {
                _elements[entry.Key] = existing.WithDeleted(existing.Deleted || deleted);
            }
            else
            {
                _elements[entry.Key] = entry.Value.WithDeleted(deleted);
            }
        }

        foreach (Dot dot in other._deleted)
        {
            MarkDeleted(dot);
        }

        _version.Merge(other._version);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(YataSequence<T> other)
    {
        Throw.IfNull(other);
        CrdtOrder elements = CompareSets(
            HasKeyNotIn(_elements, other._elements),
            HasKeyNotIn(other._elements, _elements));
        CrdtOrder deleted = CompareSets(HasNotIn(_deleted, other._deleted), HasNotIn(other._deleted, _deleted));
        return Combine(elements, deleted);
    }

    /// <inheritdoc/>
    public YataSequence<T> Clone()
    {
        return new YataSequence<T>(
            new Dictionary<Dot, Element>(_elements),
            new HashSet<Dot>(_deleted),
            _version.Clone());
    }

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out YataSequence<T> delta)
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
    public void MergeDelta(YataSequence<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(YataOperation<T> operation)
    {
        if (operation.Kind == YataOperationKind.Insert)
        {
            _version.Observe(operation.Id);
            return ApplyInsert(operation.Id, operation.OriginLeft, operation.OriginRight, operation.Value!);
        }

        _version.Observe(operation.Id);
        return MarkDeleted(operation.Id);
    }

    /// <inheritdoc/>
    public void CollectStable(StableCut cut)
    {
        Throw.IfNull(cut);

        var removable = new HashSet<Dot>();
        foreach (Dot dot in _deleted)
        {
            if (_elements.ContainsKey(dot) && cut.IsStable(dot))
            {
                removable.Add(dot);
            }
        }

        if (removable.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<Dot, Element> entry in _elements)
        {
            if (!removable.Contains(entry.Key))
            {
                removable.Remove(entry.Value.OriginLeft);
                removable.Remove(entry.Value.OriginRight);
            }
        }

        foreach (Dot dot in removable)
        {
            _elements.Remove(dot);
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
        var ids = new List<Dot>(_elements.Keys);
        ids.Sort();
        writer.WriteVarUInt64((ulong)ids.Count);
        foreach (Dot id in ids)
        {
            Element element = _elements[id];
            writer.WriteDot(id);
            writer.WriteDot(element.OriginLeft);
            writer.WriteDot(element.OriginRight);
            writer.WriteBool(element.Deleted);
            serializer.Write(ref writer, element.Value);
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
    public static YataSequence<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var sequence = new YataSequence<T>();

        int elementCount = reader.ReadCount();
        for (int i = 0; i < elementCount; i++)
        {
            Dot id = reader.ReadDot();
            Dot originLeft = reader.ReadDot();
            Dot originRight = reader.ReadDot();
            bool deleted = reader.ReadBool();
            T value = serializer.Read(ref reader);
            sequence._elements[id] = new Element(originLeft, originRight, value, deleted);
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

    /// <summary>Serializes the sequence to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The element serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var ids = new List<Dot>(_elements.Keys);
        ids.Sort();
        var deleted = new List<Dot>(_deleted);
        deleted.Sort();

        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("elements");
            foreach (Dot id in ids)
            {
                Element element = _elements[id];
                writer.WriteStartObject();
                writer.WriteString("id", DotText(id));
                writer.WriteString("originLeft", DotText(element.OriginLeft));
                writer.WriteString("originRight", DotText(element.OriginRight));
                writer.WriteBoolean("deleted", element.Deleted);
                writer.WritePropertyName("value");
                serializer.WriteJson(writer, element.Value);
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
    public static YataSequence<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var sequence = new YataSequence<T>();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        foreach (JsonElement elementJson in root.GetProperty("elements").EnumerateArray())
        {
            Dot id = ParseDot(elementJson.GetProperty("id").GetString()!);
            Dot originLeft = ParseDot(elementJson.GetProperty("originLeft").GetString()!);
            Dot originRight = ParseDot(elementJson.GetProperty("originRight").GetString()!);
            bool deleted = elementJson.GetProperty("deleted").GetBoolean();
            JsonElement valueElement = elementJson.GetProperty("value");
            var reader = new Utf8JsonReader(GetRawValueBytes(valueElement));
            reader.Read();
            T value = serializer.ReadJson(ref reader);
            sequence._elements[id] = new Element(originLeft, originRight, value, deleted);
            sequence._version.Observe(id);
            if (deleted)
            {
                sequence._deleted.Add(id);
            }
        }

        foreach (JsonElement dot in root.GetProperty("deleted").EnumerateArray())
        {
            sequence.MarkDeleted(ParseDot(dot.GetString()!));
        }

        return sequence;
    }

    /// <inheritdoc/>
    public bool Equals(YataSequence<T>? other)
    {
        if (other is null || other._elements.Count != _elements.Count || other._deleted.Count != _deleted.Count)
        {
            return false;
        }

        foreach (KeyValuePair<Dot, Element> entry in _elements)
        {
            if (!other._elements.TryGetValue(entry.Key, out Element otherElement)
                || entry.Value.Deleted != otherElement.Deleted
                || entry.Value.OriginLeft != otherElement.OriginLeft
                || entry.Value.OriginRight != otherElement.OriginRight)
            {
                return false;
            }
        }

        return _deleted.SetEquals(other._deleted);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as YataSequence<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_elements.Count, _deleted.Count);

    private bool ApplyInsert(Dot id, Dot originLeft, Dot originRight, T value)
    {
        if (_elements.ContainsKey(id))
        {
            return false;
        }

        bool deleted = _deleted.Contains(id);
        _elements[id] = new Element(originLeft, originRight, value, deleted);
        return true;
    }

    private bool MarkDeleted(Dot id)
    {
        bool added = _deleted.Add(id);
        if (_elements.TryGetValue(id, out Element element) && !element.Deleted)
        {
            _elements[id] = element.WithDeleted(true);
            return true;
        }

        return added;
    }

    private YataSequence<T> RecordDelta() => _delta ??= new YataSequence<T>();

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
        List<Dot> ordered = OrderedIds();
        var visible = new List<Dot>(ordered.Count);
        foreach (Dot id in ordered)
        {
            if (!_elements[id].Deleted && !_deleted.Contains(id))
            {
                visible.Add(id);
            }
        }

        return visible;
    }

    private List<Dot> OrderedIds()
    {
        var candidates = new List<Dot>(_elements.Keys);
        candidates.Sort();
        var integrated = new HashSet<Dot>();
        var order = new List<Dot>(candidates.Count);
        bool progressed;
        do
        {
            progressed = false;
            foreach (Dot id in candidates)
            {
                if (integrated.Contains(id) || !CanIntegrate(_elements[id], integrated))
                {
                    continue;
                }

                Element element = _elements[id];
                int left = IndexOfOrigin(order, element.OriginLeft);
                int right = IndexOfRightOrigin(order, element.OriginRight);
                if (left >= right)
                {
                    continue;
                }

                int position = FindYataPosition(order, left, right, id, element);
                order.Insert(position, id);
                integrated.Add(id);
                progressed = true;
            }
        }
        while (progressed);

        return order;
    }

    private static bool CanIntegrate(Element element, HashSet<Dot> integrated)
    {
        bool hasLeft = element.OriginLeft == Start || integrated.Contains(element.OriginLeft);
        bool hasRight = element.OriginRight == default || integrated.Contains(element.OriginRight);
        return hasLeft && hasRight;
    }

    private int FindYataPosition(List<Dot> order, int left, int right, Dot id, Element element)
    {
        int position = left + 1;
        while (position < right)
        {
            Dot currentId = order[position];
            Element current = _elements[currentId];
            if (current.OriginLeft == element.OriginLeft)
            {
                if (currentId.CompareTo(id) < 0)
                {
                    position++;
                    continue;
                }

                break;
            }

            int currentLeft = IndexOfOrigin(order, current.OriginLeft);
            if (currentLeft < left)
            {
                position++;
                continue;
            }

            break;
        }

        return position;
    }

    private static int IndexOfOrigin(List<Dot> order, Dot origin)
    {
        if (origin == Start)
        {
            return -1;
        }

        return order.IndexOf(origin);
    }

    private static int IndexOfRightOrigin(List<Dot> order, Dot origin) =>
        origin == default ? order.Count : order.IndexOf(origin);

    private static bool HasKeyNotIn(Dictionary<Dot, Element> source, Dictionary<Dot, Element> other)
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
        var replica = new ReplicaId(SpanCompat.ParseGuidExactN(text.AsSpan(0, separator)));
        ulong sequence = SpanCompat.ParseUInt64Invariant(text.AsSpan(separator + 1));
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

    private readonly struct Element
    {
        public Element(Dot originLeft, Dot originRight, T value, bool deleted)
        {
            OriginLeft = originLeft;
            OriginRight = originRight;
            Value = value;
            Deleted = deleted;
        }

        public Dot OriginLeft { get; }

        public Dot OriginRight { get; }

        public T Value { get; }

        public bool Deleted { get; }

        public Element WithDeleted(bool deleted) => new(OriginLeft, OriginRight, Value, deleted);
    }
}
