// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A WOOT sequence CRDT for collaboratively edited ordered collections. Each W-character has
/// a unique <see cref="Dot"/> id, a value, and immutable previous/next structural neighbours.
/// Concurrent insertions between the same neighbours are integrated by deterministic dot
/// precedence while deletions only hide characters as tombstones.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
/// <remarks>
/// State is a grow-only set of W-characters plus grow-only invisibility tombstones. Merging
/// unions character ids and logically ORs visibility, so the state forms a join-semilattice.
/// Operation application is idempotent and order-tolerant: a character is retained but omitted
/// from the rendered order until both non-sentinel neighbours are known. Mutable and not
/// thread-safe.
/// </remarks>
public sealed class WootSequence<T> :
    IConvergent<WootSequence<T>>,
    IDeltaConvergent<WootSequence<T>, WootSequence<T>>,
    IOperationConvergent<WootOperation<T>>,
    IEquatable<WootSequence<T>>
{
    private static Dot Begin => default;

    private static Dot End => new(default, ulong.MaxValue);

    private readonly Dictionary<Dot, Character> _characters;
    private readonly HashSet<Dot> _deleted;
    private readonly VersionVector _version;
    private WootSequence<T>? _delta;

    /// <summary>Initializes an empty sequence.</summary>
    public WootSequence()
    {
        _characters = [];
        _deleted = [];
        _version = new VersionVector();
    }

    private WootSequence(Dictionary<Dot, Character> characters, HashSet<Dot> deleted, VersionVector version)
    {
        _characters = characters;
        _deleted = deleted;
        _version = version;
    }

    /// <summary>Gets the number of visible characters.</summary>
    public int Count => VisibleIds().Count;

    /// <summary>Gets the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The zero-based visible index.</param>
    /// <returns>The element value.</returns>
    public T this[int index] => _characters[VisibleIdAt(index)].Value;

    /// <summary>Returns the visible elements in sequence order.</summary>
    /// <returns>An array of visible element values.</returns>
    public T[] ToArray()
    {
        List<Dot> ids = VisibleIds();
        var values = new T[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            values[i] = _characters[ids[i]].Value;
        }

        return values;
    }

    /// <summary>Inserts <paramref name="value"/> at <paramref name="index"/> on behalf of a replica.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="index">The visible position at which to insert (0..Count).</param>
    /// <param name="value">The value to insert.</param>
    /// <returns>The operation to broadcast.</returns>
    public WootOperation<T> Insert(ReplicaId replica, int index, T value)
    {
        List<Dot> ids = VisibleIds();
        if (index < 0 || index > ids.Count)
        {
            Throw.ArgumentOutOfRange(nameof(index), "Index is outside the sequence bounds.");
        }

        Dot prevId = index == 0 ? Begin : ids[index - 1];
        Dot nextId = index == ids.Count ? End : ids[index];
        Dot id = _version.Increment(replica);
        ApplyInsert(id, prevId, nextId, value);
        RecordDelta().ApplyInsert(id, prevId, nextId, value);
        return WootOperation<T>.Insert(id, prevId, nextId, value);
    }

    /// <summary>Appends <paramref name="value"/> to the end of the sequence.</summary>
    /// <param name="replica">The local replica.</param>
    /// <param name="value">The value to append.</param>
    /// <returns>The operation to broadcast.</returns>
    public WootOperation<T> Append(ReplicaId replica, T value) => Insert(replica, Count, value);

    /// <summary>Deletes the visible element at <paramref name="index"/>.</summary>
    /// <param name="index">The visible position to delete (0..Count-1).</param>
    /// <returns>The operation to broadcast.</returns>
    public WootOperation<T> Delete(int index)
    {
        Dot id = VisibleIdAt(index);
        MarkDeleted(id);
        RecordDelta().MarkDeleted(id);
        return WootOperation<T>.Delete(id);
    }

    /// <inheritdoc/>
    public void Merge(WootSequence<T> other)
    {
        Throw.IfNull(other);
        foreach (KeyValuePair<Dot, Character> entry in other._characters)
        {
            bool visible = entry.Value.Visible && !_deleted.Contains(entry.Key) && !other._deleted.Contains(entry.Key);
            if (!visible)
            {
                _deleted.Add(entry.Key);
            }

            if (_characters.TryGetValue(entry.Key, out Character existing))
            {
                _characters[entry.Key] = existing.WithVisible(existing.Visible && visible);
            }
            else
            {
                _characters[entry.Key] = entry.Value.WithVisible(visible);
            }
        }

        foreach (Dot dot in other._deleted)
        {
            MarkDeleted(dot);
        }

        _version.Merge(other._version);
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(WootSequence<T> other)
    {
        Throw.IfNull(other);
        CrdtOrder characters = CompareSets(
            HasKeyNotIn(_characters, other._characters),
            HasKeyNotIn(other._characters, _characters));
        CrdtOrder deleted = CompareSets(HasNotIn(_deleted, other._deleted), HasNotIn(other._deleted, _deleted));
        return Combine(characters, deleted);
    }

    /// <inheritdoc/>
    public WootSequence<T> Clone()
    {
        return new WootSequence<T>(
            new Dictionary<Dot, Character>(_characters),
            new HashSet<Dot>(_deleted),
            _version.Clone());
    }

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out WootSequence<T> delta)
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
    public void MergeDelta(WootSequence<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(WootOperation<T> operation)
    {
        if (operation.Kind == WootOperationKind.Insert)
        {
            _version.Observe(operation.Id);
            return ApplyInsert(operation.Id, operation.PrevId, operation.NextId, operation.Value!);
        }

        _version.Observe(operation.Id);
        return MarkDeleted(operation.Id);
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
        var ids = new List<Dot>(_characters.Keys);
        ids.Sort();
        writer.WriteVarUInt64((ulong)ids.Count);
        foreach (Dot id in ids)
        {
            Character character = _characters[id];
            writer.WriteDot(id);
            writer.WriteDot(character.PrevId);
            writer.WriteDot(character.NextId);
            writer.WriteBool(character.Visible);
            serializer.Write(ref writer, character.Value);
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
    public static WootSequence<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        var sequence = new WootSequence<T>();

        int characterCount = reader.ReadCount();
        for (int i = 0; i < characterCount; i++)
        {
            Dot id = reader.ReadDot();
            Dot prevId = reader.ReadDot();
            Dot nextId = reader.ReadDot();
            bool visible = reader.ReadBool();
            T value = serializer.Read(ref reader);
            sequence._characters[id] = new Character(prevId, nextId, value, visible);
            if (!visible)
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
        var ids = new List<Dot>(_characters.Keys);
        ids.Sort();
        var deleted = new List<Dot>(_deleted);
        deleted.Sort();

        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("characters");
            foreach (Dot id in ids)
            {
                Character character = _characters[id];
                writer.WriteStartObject();
                writer.WriteString("id", DotText(id));
                writer.WriteString("prevId", DotText(character.PrevId));
                writer.WriteString("nextId", DotText(character.NextId));
                writer.WriteBoolean("visible", character.Visible);
                writer.WritePropertyName("value");
                serializer.WriteJson(writer, character.Value);
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
    public static WootSequence<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var sequence = new WootSequence<T>();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        foreach (JsonElement elementJson in root.GetProperty("characters").EnumerateArray())
        {
            Dot id = ParseDot(elementJson.GetProperty("id").GetString()!);
            Dot prevId = ParseDot(elementJson.GetProperty("prevId").GetString()!);
            Dot nextId = ParseDot(elementJson.GetProperty("nextId").GetString()!);
            bool visible = elementJson.GetProperty("visible").GetBoolean();
            JsonElement valueElement = elementJson.GetProperty("value");
            var reader = new Utf8JsonReader(GetRawValueBytes(valueElement));
            reader.Read();
            T value = serializer.ReadJson(ref reader);
            sequence._characters[id] = new Character(prevId, nextId, value, visible);
            sequence._version.Observe(id);
            if (!visible)
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
    public bool Equals(WootSequence<T>? other)
    {
        if (other is null || other._characters.Count != _characters.Count || other._deleted.Count != _deleted.Count)
        {
            return false;
        }

        foreach (KeyValuePair<Dot, Character> entry in _characters)
        {
            if (!other._characters.TryGetValue(entry.Key, out Character otherCharacter)
                || entry.Value.Visible != otherCharacter.Visible
                || entry.Value.PrevId != otherCharacter.PrevId
                || entry.Value.NextId != otherCharacter.NextId)
            {
                return false;
            }
        }

        return _deleted.SetEquals(other._deleted);
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as WootSequence<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_characters.Count, _deleted.Count);

    private bool ApplyInsert(Dot id, Dot prevId, Dot nextId, T value)
    {
        if (_characters.ContainsKey(id))
        {
            return false;
        }

        bool visible = !_deleted.Contains(id);
        _characters[id] = new Character(prevId, nextId, value, visible);
        return true;
    }

    private bool MarkDeleted(Dot id)
    {
        bool added = _deleted.Add(id);
        if (_characters.TryGetValue(id, out Character character) && character.Visible)
        {
            _characters[id] = character.WithVisible(false);
            return true;
        }

        return added;
    }

    private WootSequence<T> RecordDelta() => _delta ??= new WootSequence<T>();

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
            Character character = _characters[id];
            if (character.Visible && !_deleted.Contains(id))
            {
                visible.Add(id);
            }
        }

        return visible;
    }

    private List<Dot> OrderedIds()
    {
        var candidates = new List<Dot>(_characters.Keys);
        candidates.Sort();
        var integrated = new HashSet<Dot>();
        var order = new List<Dot>(candidates.Count);
        bool progressed;
        do
        {
            progressed = false;
            foreach (Dot id in candidates)
            {
                if (integrated.Contains(id) || !CanIntegrate(_characters[id], integrated))
                {
                    continue;
                }

                Character character = _characters[id];
                int left = IndexOfPrev(order, character.PrevId);
                int right = IndexOfNext(order, character.NextId);
                if (left >= right)
                {
                    continue;
                }

                int position = FindWootPosition(order, left, right, id);
                order.Insert(position, id);
                integrated.Add(id);
                progressed = true;
            }
        }
        while (progressed);

        return order;
    }

    private static bool CanIntegrate(Character character, HashSet<Dot> integrated)
    {
        bool hasPrev = character.PrevId == Begin || integrated.Contains(character.PrevId);
        bool hasNext = character.NextId == End || integrated.Contains(character.NextId);
        return hasPrev && hasNext;
    }

    private static int FindWootPosition(List<Dot> order, int left, int right, Dot id)
    {
        int position = left + 1;
        while (position < right && order[position].CompareTo(id) < 0)
        {
            position++;
        }

        return position;
    }

    private static int IndexOfPrev(List<Dot> order, Dot id)
    {
        if (id == Begin)
        {
            return -1;
        }

        return order.IndexOf(id);
    }

    private static int IndexOfNext(List<Dot> order, Dot id) => id == End ? order.Count : order.IndexOf(id);

    private static bool HasKeyNotIn(Dictionary<Dot, Character> source, Dictionary<Dot, Character> other)
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

    private readonly struct Character
    {
        public Character(Dot prevId, Dot nextId, T value, bool visible)
        {
            PrevId = prevId;
            NextId = nextId;
            Value = value;
            Visible = visible;
        }

        public Dot PrevId { get; }

        public Dot NextId { get; }

        public T Value { get; }

        public bool Visible { get; }

        public Character WithVisible(bool visible) => new(PrevId, NextId, Value, visible);
    }
}
