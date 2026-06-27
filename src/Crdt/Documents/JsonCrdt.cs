// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A reflection-free JSON CRDT inspired by Kleppmann and Beresford's JSON CRDT. The root is
/// always a map node; maps use add-wins observed-remove presence plus last-writer-wins
/// assignment metadata, lists use RGA-style element ids and tombstones, and primitive leaves
/// use last-writer-wins registers.
/// </summary>
/// <remarks>
/// This first version is intentionally bounded: it supports JSON objects, arrays, and primitive
/// leaves (<see langword="string"/>, <see cref="double"/>, <see cref="bool"/>, and
/// <see langword="null"/>), stores numbers as <see cref="double"/>, and addresses array
/// elements by stable element ids rather than by mutable numeric indexes.
/// </remarks>
public sealed partial class JsonCrdt :
    IConvergent<JsonCrdt>,
    IOperationConvergent<JsonOperation>,
    IEquatable<JsonCrdt>,
    IBinaryWritable
{
    private readonly VersionVector _version;
    private readonly HashSet<Dot> _operations;
    private readonly MapNode _root;

    /// <summary>Initializes an empty JSON CRDT document with a map root.</summary>
    public JsonCrdt()
    {
        _version = new VersionVector();
        _operations = [];
        _root = new MapNode();
    }

    private JsonCrdt(MapNode root, VersionVector version, HashSet<Dot> operations)
    {
        _root = root;
        _version = version;
        _operations = operations;
    }

    /// <summary>Gets the root map node.</summary>
    public JsonNode Root => _root;

    /// <summary>Sets a string property on the root map.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="key">The property key.</param>
    /// <param name="value">The string value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetString(ReplicaId replica, Timestamp timestamp, string key, string value) =>
        SetString(replica, timestamp, Array.Empty<JsonPathSegment>(), key, value);

    /// <summary>Sets a string property on a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The property key.</param>
    /// <param name="value">The string value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetString(
        ReplicaId replica,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        string key,
        string value) => SetKey(replica, timestamp, path, key, JsonLiteral.PrimitiveValue(JsonPrimitive.String(value)));

    /// <summary>Sets a number property on the root map.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="key">The property key.</param>
    /// <param name="value">The number value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetNumber(ReplicaId replica, Timestamp timestamp, string key, double value) =>
        SetNumber(replica, timestamp, Array.Empty<JsonPathSegment>(), key, value);

    /// <summary>Sets a number property on a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The property key.</param>
    /// <param name="value">The number value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetNumber(
        ReplicaId replica,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        string key,
        double value) => SetKey(replica, timestamp, path, key, JsonLiteral.PrimitiveValue(JsonPrimitive.Number(value)));

    /// <summary>Sets a boolean property on a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The property key.</param>
    /// <param name="value">The boolean value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetBoolean(
        ReplicaId replica,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        string key,
        bool value) => SetKey(replica, timestamp, path, key, JsonLiteral.PrimitiveValue(JsonPrimitive.Boolean(value)));

    /// <summary>Sets a null property on a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetNull(ReplicaId replica, Timestamp timestamp, IEnumerable<JsonPathSegment> path, string key) =>
        SetKey(replica, timestamp, path, key, JsonLiteral.PrimitiveValue(JsonPrimitive.Null));

    /// <summary>Sets an object property on a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetObject(ReplicaId replica, Timestamp timestamp, IEnumerable<JsonPathSegment> path, string key) =>
        SetKey(replica, timestamp, path, key, JsonLiteral.EmptyObject);

    /// <summary>Sets an array property on a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The property key.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetArray(ReplicaId replica, Timestamp timestamp, IEnumerable<JsonPathSegment> path, string key) =>
        SetKey(replica, timestamp, path, key, JsonLiteral.EmptyArray);

    /// <summary>Sets a literal property on a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The property key.</param>
    /// <param name="value">The literal value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation SetKey(
        ReplicaId replica,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        string key,
        JsonLiteral value)
    {
        Dot dot = _version.Increment(replica);
        var operation = JsonOperation.SetKey(dot, timestamp, path, key, value);
        Apply(operation);
        return operation;
    }

    /// <summary>Appends an object to a list node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The insert timestamp.</param>
    /// <param name="path">The target list path.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation PushObject(ReplicaId replica, Timestamp timestamp, IEnumerable<JsonPathSegment> path) =>
        InsertAfter(replica, timestamp, path, LastElementId(path), JsonLiteral.EmptyObject);

    /// <summary>Appends a literal to a list node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The insert timestamp.</param>
    /// <param name="path">The target list path.</param>
    /// <param name="value">The literal value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation Push(ReplicaId replica, Timestamp timestamp, IEnumerable<JsonPathSegment> path, JsonLiteral value) =>
        InsertAfter(replica, timestamp, path, LastElementId(path), value);

    /// <summary>Inserts a literal into a list after an element id, or at the head for default.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The insert timestamp.</param>
    /// <param name="path">The target list path.</param>
    /// <param name="afterElementId">The predecessor element id, or default for head.</param>
    /// <param name="value">The literal value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation InsertAfter(
        ReplicaId replica,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        Dot afterElementId,
        JsonLiteral value)
    {
        Dot dot = _version.Increment(replica);
        var operation = JsonOperation.InsertAfter(dot, timestamp, path, afterElementId, value);
        Apply(operation);
        return operation;
    }

    /// <summary>Deletes a key from a map node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The deletion timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The key to delete.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation DeleteKey(ReplicaId replica, Timestamp timestamp, IEnumerable<JsonPathSegment> path, string key)
    {
        Dot dot = _version.Increment(replica);
        var removed = new List<Dot>();
        if (Navigate(path) is MapNode map)
        {
            removed.AddRange(map.DotsFor(key));
        }

        var operation = JsonOperation.DeleteKey(dot, timestamp, path, key, removed);
        Apply(operation);
        return operation;
    }

    /// <summary>Deletes an element from a list node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The deletion timestamp.</param>
    /// <param name="path">The target list path.</param>
    /// <param name="elementId">The element id to delete.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation DeleteIndex(ReplicaId replica, Timestamp timestamp, IEnumerable<JsonPathSegment> path, Dot elementId)
    {
        Dot dot = _version.Increment(replica);
        var operation = JsonOperation.DeleteIndex(dot, timestamp, path, elementId);
        Apply(operation);
        return operation;
    }

    /// <summary>Assigns a primitive value to a register node.</summary>
    /// <param name="replica">The local replica id.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <param name="path">The target register path.</param>
    /// <param name="primitive">The assigned primitive value.</param>
    /// <returns>The operation to broadcast.</returns>
    public JsonOperation Assign(
        ReplicaId replica,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        JsonPrimitive primitive)
    {
        Dot dot = _version.Increment(replica);
        var operation = JsonOperation.Assign(dot, timestamp, path, primitive);
        Apply(operation);
        return operation;
    }

    /// <summary>Gets visible list element ids for the list at <paramref name="path"/>.</summary>
    /// <param name="path">The list path.</param>
    /// <returns>The visible element ids in document order.</returns>
    public IReadOnlyList<Dot> ListElementIds(IEnumerable<JsonPathSegment> path) =>
        Navigate(path) is ListNode list ? list.VisibleIds() : Array.Empty<Dot>();

    /// <inheritdoc/>
    public void Merge(JsonCrdt other)
    {
        Throw.IfNull(other);
        _root.Merge(other._root);
        _version.Merge(other._version);
        foreach (Dot dot in other._operations)
        {
            _operations.Add(dot);
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(JsonCrdt other)
    {
        Throw.IfNull(other);
        if (Equals(other))
        {
            return CrdtOrder.Equal;
        }

        JsonCrdt left = Clone();
        left.Merge(other);
        if (left.Equals(other))
        {
            return CrdtOrder.Less;
        }

        JsonCrdt right = other.Clone();
        right.Merge(this);
        return right.Equals(this) ? CrdtOrder.Greater : CrdtOrder.Concurrent;
    }

    /// <inheritdoc/>
    public JsonCrdt Clone() => new((MapNode)_root.Clone(), _version.Clone(), new HashSet<Dot>(_operations));

    /// <inheritdoc/>
    public bool Apply(JsonOperation operation)
    {
        Throw.IfNull(operation);
        _version.Observe(operation.Dot);
        if (!_operations.Add(operation.Dot))
        {
            return false;
        }

        JsonNode? node = Navigate(operation.Path);
        if (operation.Kind == JsonOperationKind.SetKey && node is MapNode map && operation.Value is not null)
        {
            return map.ApplySet(operation.Key, BuildNode(operation.Value, operation.Dot, operation.Timestamp), operation.Dot);
        }

        if (operation.Kind == JsonOperationKind.DeleteKey && node is MapNode deleteMap)
        {
            return deleteMap.ApplyDelete(operation.Key, operation.RemovedDots);
        }

        if (operation.Kind == JsonOperationKind.InsertAfter && node is ListNode list && operation.Value is not null)
        {
            return list.ApplyInsert(operation.ElementId, operation.AfterElementId, BuildNode(operation.Value, operation.Dot, operation.Timestamp));
        }

        if (operation.Kind == JsonOperationKind.DeleteIndex && node is ListNode deleteList)
        {
            return deleteList.ApplyDelete(operation.ElementId);
        }

        if (operation.Kind == JsonOperationKind.Assign && node is RegisterNode register)
        {
            return register.ApplyAssign(operation.Primitive, operation.Timestamp);
        }

        return false;
    }

    /// <summary>Serializes this document to a new byte array.</summary>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray() => CrdtBinary.ToByteArray(this);

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte(CrdtBinary.FormatVersion);
        _root.Write(ref writer);
        _version.Write(ref writer);
        var operations = new List<Dot>(_operations);
        operations.Sort();
        writer.WriteVarUInt64((ulong)operations.Count);
        foreach (Dot operation in operations)
        {
            writer.WriteDot(operation);
        }
    }

    /// <summary>Decodes a JSON CRDT document from binary state.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded document.</returns>
    public static JsonCrdt ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    /// <summary>Decodes a JSON CRDT document from binary state.</summary>
    /// <param name="reader">The source reader.</param>
    /// <returns>The decoded document.</returns>
    public static JsonCrdt Read(ref CrdtReader reader)
    {
        byte version = reader.ReadByte();
        if (version != CrdtBinary.FormatVersion)
        {
            Throw.InvalidData<JsonCrdt>("Unsupported JSON CRDT binary format version.");
        }

        JsonNode node = JsonNode.Read(ref reader);
        if (node is not MapNode root)
        {
            return Throw.InvalidData<JsonCrdt>("JSON CRDT root must be a map node.");
        }

        VersionVector vector = VersionVector.Read(ref reader);
        int operationCount = reader.ReadCount();
        var operations = new HashSet<Dot>();
        for (int i = 0; i < operationCount; i++)
        {
            operations.Add(reader.ReadDot());
        }

        return new JsonCrdt(root, vector, operations);
    }

    /// <inheritdoc/>
    public bool Equals(JsonCrdt? other) => other is not null && _root.Equals(other._root)
        && _version.Equals(other._version) && _operations.SetEquals(other._operations);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as JsonCrdt);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = HashCode.Combine(_root, _version);
        foreach (Dot dot in _operations)
        {
            hash ^= dot.GetHashCode();
        }

        return hash;
    }

    private Dot LastElementId(IEnumerable<JsonPathSegment> path)
    {
        if (Navigate(path) is ListNode list)
        {
            List<Dot> ids = list.VisibleIds();
            return ids.Count == 0 ? default : ids[ids.Count - 1];
        }

        return default;
    }

    private JsonNode? Navigate(IEnumerable<JsonPathSegment> path)
    {
        JsonNode node = _root;
        foreach (JsonPathSegment segment in path)
        {
            if (segment.Kind == JsonPathSegmentKind.Key)
            {
                if (node is not MapNode map || !map.TryGetValue(segment.Key, out JsonNode? child))
                {
                    return null;
                }

                node = child;
            }
            else
            {
                if (node is not ListNode list || !list.TryGetElement(segment.ElementId, out JsonNode? child))
                {
                    return null;
                }

                node = child;
            }
        }

        return node;
    }

    private static JsonNode BuildNode(JsonLiteral literal, Dot dot, Timestamp timestamp)
    {
        if (literal.Kind == JsonLiteralKind.Primitive)
        {
            return new RegisterNode(literal.Primitive, timestamp);
        }

        if (literal.Kind == JsonLiteralKind.Object)
        {
            var map = new MapNode();
            foreach (KeyValuePair<string, JsonLiteral> property in literal.Properties)
            {
                map.ApplySet(property.Key, BuildNode(property.Value, dot, timestamp), dot);
            }

            return map;
        }

        var list = new ListNode();
        Dot after = default;
        ulong offset = 0UL;
        foreach (JsonLiteral item in literal.Items)
        {
            var childDot = new Dot(dot.Replica, dot.Sequence + (++offset));
            list.ApplyInsert(childDot, after, BuildNode(item, childDot, timestamp));
            after = childDot;
        }

        return list;
    }

    private static bool ShouldAccept(Timestamp incoming, Timestamp current) => incoming > current;

    /// <summary>The base type for JSON CRDT nodes.</summary>
    public abstract class JsonNode : IEquatable<JsonNode>
    {
        /// <summary>Gets the node kind.</summary>
        public abstract JsonLiteralKind Kind { get; }

        internal abstract JsonNode Clone();

        internal abstract void Merge(JsonNode other);

        internal abstract void Write(ref CrdtWriter writer);

        internal abstract void WriteJson(Utf8JsonWriter writer);

        internal static JsonNode Read(ref CrdtReader reader)
        {
            var kind = (JsonLiteralKind)reader.ReadByte();
            if (kind == JsonLiteralKind.Object)
            {
                return MapNode.ReadMap(ref reader);
            }

            if (kind == JsonLiteralKind.Array)
            {
                return ListNode.ReadList(ref reader);
            }

            return RegisterNode.ReadRegister(ref reader);
        }

        /// <inheritdoc/>
        public abstract bool Equals(JsonNode? other);

        /// <inheritdoc/>
        public abstract override bool Equals([NotNullWhen(true)] object? obj);

        /// <inheritdoc/>
        public abstract override int GetHashCode();
    }
}
