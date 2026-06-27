// Copyright (c) marcschier. Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace Crdt;

/// <summary>Identifies the JSON CRDT operation kind.</summary>
public enum JsonOperationKind
{
    /// <summary>Assigns a map key to a JSON literal.</summary>
    SetKey = 0,

    /// <summary>Inserts a JSON literal into a list after an existing element, or at the head.</summary>
    InsertAfter = 1,

    /// <summary>Removes an observed map key assignment.</summary>
    DeleteKey = 2,

    /// <summary>Removes a list element by its stable element id.</summary>
    DeleteIndex = 3,

    /// <summary>Assigns a primitive value to a register node.</summary>
    Assign = 4,
}

/// <summary>Identifies the kind of a path segment in a <see cref="JsonOperation"/>.</summary>
public enum JsonPathSegmentKind
{
    /// <summary>A map key segment.</summary>
    Key = 0,

    /// <summary>A list element-id segment.</summary>
    Element = 1,
}

/// <summary>Identifies the kind of a JSON primitive stored by a register node.</summary>
public enum JsonPrimitiveKind
{
    /// <summary>The JSON <see langword="null"/> value.</summary>
    Null = 0,

    /// <summary>A JSON string value.</summary>
    String = 1,

    /// <summary>A JSON number represented as <see cref="double"/>.</summary>
    Number = 2,

    /// <summary>A JSON boolean value.</summary>
    Boolean = 3,
}

/// <summary>Identifies the JSON literal kind carried by a JSON CRDT operation.</summary>
public enum JsonLiteralKind
{
    /// <summary>A JSON object literal.</summary>
    Object = 0,

    /// <summary>A JSON array literal.</summary>
    Array = 1,

    /// <summary>A primitive register literal.</summary>
    Primitive = 2,
}

/// <summary>A single address segment in a JSON CRDT operation path.</summary>
public readonly struct JsonPathSegment : IEquatable<JsonPathSegment>
{
    /// <summary>Initializes a new map-key path segment.</summary>
    /// <param name="key">The map key.</param>
    public JsonPathSegment(string key)
    {
        Throw.IfNull(key);
        Kind = JsonPathSegmentKind.Key;
        Key = key;
        ElementId = default;
    }

    /// <summary>Initializes a new list-element path segment.</summary>
    /// <param name="elementId">The list element id.</param>
    public JsonPathSegment(Dot elementId)
    {
        Kind = JsonPathSegmentKind.Element;
        Key = string.Empty;
        ElementId = elementId;
    }

    private JsonPathSegment(JsonPathSegmentKind kind, string key, Dot elementId)
    {
        Kind = kind;
        Key = key;
        ElementId = elementId;
    }

    /// <summary>Gets the path segment kind.</summary>
    public JsonPathSegmentKind Kind { get; }

    /// <summary>Gets the map key for <see cref="JsonPathSegmentKind.Key"/> segments.</summary>
    public string Key { get; }

    /// <summary>Gets the list element id for <see cref="JsonPathSegmentKind.Element"/> segments.</summary>
    public Dot ElementId { get; }

    /// <summary>Creates a map-key path segment.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The created path segment.</returns>
    public static JsonPathSegment MapKey(string key) => new(key);

    /// <summary>Creates a list-element path segment.</summary>
    /// <param name="elementId">The element id.</param>
    /// <returns>The created path segment.</returns>
    public static JsonPathSegment ListElement(Dot elementId) => new(elementId);

    /// <inheritdoc/>
    public bool Equals(JsonPathSegment other) => Kind == other.Kind
        && string.Equals(Key, other.Key, StringComparison.Ordinal) && ElementId.Equals(other.ElementId);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsonPathSegment other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Key, ElementId);

    /// <summary>Equality operator.</summary>
    public static bool operator ==(JsonPathSegment left, JsonPathSegment right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(JsonPathSegment left, JsonPathSegment right) => !left.Equals(right);

    internal void Write(ref CrdtWriter writer)
    {
        writer.WriteByte((byte)Kind);
        if (Kind == JsonPathSegmentKind.Key)
        {
            writer.WriteString(Key);
        }
        else
        {
            writer.WriteDot(ElementId);
        }
    }

    internal static JsonPathSegment Read(ref CrdtReader reader)
    {
        var kind = (JsonPathSegmentKind)reader.ReadByte();
        return kind == JsonPathSegmentKind.Key
            ? new JsonPathSegment(kind, reader.ReadString() ?? string.Empty, default)
            : new JsonPathSegment(kind, string.Empty, reader.ReadDot());
    }
}

/// <summary>A primitive JSON value stored by a JSON CRDT register node.</summary>
public readonly struct JsonPrimitive : IEquatable<JsonPrimitive>
{
    private readonly string? _stringValue;
    private readonly double _numberValue;
    private readonly bool _booleanValue;

    private JsonPrimitive(JsonPrimitiveKind kind, string? stringValue, double numberValue, bool booleanValue)
    {
        Kind = kind;
        _stringValue = stringValue;
        _numberValue = numberValue;
        _booleanValue = booleanValue;
    }

    /// <summary>Gets the primitive kind.</summary>
    public JsonPrimitiveKind Kind { get; }

    /// <summary>Gets a JSON <see langword="null"/> primitive.</summary>
    public static JsonPrimitive Null => default;

    /// <summary>Creates a JSON string primitive.</summary>
    /// <param name="value">The string value.</param>
    /// <returns>The primitive value.</returns>
    public static JsonPrimitive String(string value)
    {
        Throw.IfNull(value);
        return new JsonPrimitive(JsonPrimitiveKind.String, value, 0D, false);
    }

    /// <summary>Creates a JSON number primitive.</summary>
    /// <param name="value">The number value.</param>
    /// <returns>The primitive value.</returns>
    public static JsonPrimitive Number(double value) => new(JsonPrimitiveKind.Number, null, value, false);

    /// <summary>Creates a JSON boolean primitive.</summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>The primitive value.</returns>
    public static JsonPrimitive Boolean(bool value) => new(JsonPrimitiveKind.Boolean, null, 0D, value);

    /// <summary>Gets the string value.</summary>
    /// <returns>The string value.</returns>
    /// <exception cref="InvalidOperationException">The primitive is not a string.</exception>
    public string GetString() => Kind == JsonPrimitiveKind.String
        ? _stringValue!
        : throw new InvalidOperationException("The JSON primitive is not a string.");

    /// <summary>Gets the number value.</summary>
    /// <returns>The number value.</returns>
    /// <exception cref="InvalidOperationException">The primitive is not a number.</exception>
    public double GetNumber() => Kind == JsonPrimitiveKind.Number
        ? _numberValue
        : throw new InvalidOperationException("The JSON primitive is not a number.");

    /// <summary>Gets the boolean value.</summary>
    /// <returns>The boolean value.</returns>
    /// <exception cref="InvalidOperationException">The primitive is not a boolean.</exception>
    public bool GetBoolean() => Kind == JsonPrimitiveKind.Boolean
        ? _booleanValue
        : throw new InvalidOperationException("The JSON primitive is not a boolean.");

    /// <inheritdoc/>
    public bool Equals(JsonPrimitive other) => Kind == other.Kind && Kind switch
    {
        JsonPrimitiveKind.String => string.Equals(_stringValue, other._stringValue, StringComparison.Ordinal),
        JsonPrimitiveKind.Number => _numberValue.Equals(other._numberValue),
        JsonPrimitiveKind.Boolean => _booleanValue == other._booleanValue,
        _ => true,
    };

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is JsonPrimitive other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Kind switch
    {
        JsonPrimitiveKind.String => HashCode.Combine(Kind, _stringValue),
        JsonPrimitiveKind.Number => HashCode.Combine(Kind, _numberValue),
        JsonPrimitiveKind.Boolean => HashCode.Combine(Kind, _booleanValue),
        _ => 0,
    };

    /// <summary>Equality operator.</summary>
    public static bool operator ==(JsonPrimitive left, JsonPrimitive right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(JsonPrimitive left, JsonPrimitive right) => !left.Equals(right);

    internal void Write(ref CrdtWriter writer)
    {
        writer.WriteByte((byte)Kind);
        if (Kind == JsonPrimitiveKind.String)
        {
            writer.WriteString(_stringValue);
        }
        else if (Kind == JsonPrimitiveKind.Number)
        {
            writer.WriteRaw(BitConverter.GetBytes(_numberValue));
        }
        else if (Kind == JsonPrimitiveKind.Boolean)
        {
            writer.WriteBool(_booleanValue);
        }
    }

    internal static JsonPrimitive Read(ref CrdtReader reader)
    {
        var kind = (JsonPrimitiveKind)reader.ReadByte();
        if (kind == JsonPrimitiveKind.String)
        {
            return String(reader.ReadString() ?? string.Empty);
        }

        if (kind == JsonPrimitiveKind.Number)
        {
            return Number(BitConverter.ToDouble(reader.ReadRaw(sizeof(double)).ToArray(), 0));
        }

        return kind == JsonPrimitiveKind.Boolean ? Boolean(reader.ReadBool()) : Null;
    }
}

/// <summary>A JSON literal carried by a JSON CRDT operation.</summary>
public sealed class JsonLiteral : IEquatable<JsonLiteral>, IBinaryWritable
{
    private readonly JsonPrimitive _primitive;
    private readonly IReadOnlyList<KeyValuePair<string, JsonLiteral>> _properties;
    private readonly IReadOnlyList<JsonLiteral> _items;

    private JsonLiteral(
        JsonLiteralKind kind,
        JsonPrimitive primitive,
        IReadOnlyList<KeyValuePair<string, JsonLiteral>> properties,
        IReadOnlyList<JsonLiteral> items)
    {
        Kind = kind;
        _primitive = primitive;
        _properties = properties;
        _items = items;
    }

    /// <summary>Gets the literal kind.</summary>
    public JsonLiteralKind Kind { get; }

    /// <summary>Gets an empty object literal.</summary>
    public static JsonLiteral EmptyObject { get; } =
        Object(global::System.Array.Empty<KeyValuePair<string, JsonLiteral>>());

    /// <summary>Gets an empty array literal.</summary>
    public static JsonLiteral EmptyArray { get; } = Array(global::System.Array.Empty<JsonLiteral>());

    /// <summary>Gets the primitive value for primitive literals.</summary>
    public JsonPrimitive Primitive => _primitive;

    /// <summary>Gets object properties sorted by ordinal key.</summary>
    public IReadOnlyList<KeyValuePair<string, JsonLiteral>> Properties => _properties;

    /// <summary>Gets array items in literal order.</summary>
    public IReadOnlyList<JsonLiteral> Items => _items;

    /// <summary>Creates a primitive literal.</summary>
    /// <param name="primitive">The primitive value.</param>
    /// <returns>The literal.</returns>
    public static JsonLiteral PrimitiveValue(JsonPrimitive primitive) =>
        new(
            JsonLiteralKind.Primitive,
            primitive,
            global::System.Array.Empty<KeyValuePair<string, JsonLiteral>>(),
            global::System.Array.Empty<JsonLiteral>());

    /// <summary>Creates an object literal.</summary>
    /// <param name="properties">The object properties.</param>
    /// <returns>The literal.</returns>
    public static JsonLiteral Object(IEnumerable<KeyValuePair<string, JsonLiteral>> properties)
    {
        Throw.IfNull(properties);
        var list = new List<KeyValuePair<string, JsonLiteral>>(properties);
        list.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));
        return new JsonLiteral(
            JsonLiteralKind.Object, JsonPrimitive.Null, list, global::System.Array.Empty<JsonLiteral>());
    }

    /// <summary>Creates an array literal.</summary>
    /// <param name="items">The array items.</param>
    /// <returns>The literal.</returns>
    public static JsonLiteral Array(IEnumerable<JsonLiteral> items)
    {
        Throw.IfNull(items);
        return new JsonLiteral(
            JsonLiteralKind.Array,
            JsonPrimitive.Null,
            global::System.Array.Empty<KeyValuePair<string, JsonLiteral>>(),
            new List<JsonLiteral>(items));
    }

    /// <summary>Serializes this literal to a new byte array.</summary>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray() => CrdtBinary.ToByteArray(this);

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte((byte)Kind);
        if (Kind == JsonLiteralKind.Primitive)
        {
            _primitive.Write(ref writer);
        }
        else if (Kind == JsonLiteralKind.Object)
        {
            writer.WriteVarUInt64((ulong)_properties.Count);
            foreach (KeyValuePair<string, JsonLiteral> property in _properties)
            {
                writer.WriteString(property.Key);
                property.Value.Write(ref writer);
            }
        }
        else
        {
            writer.WriteVarUInt64((ulong)_items.Count);
            foreach (JsonLiteral item in _items)
            {
                item.Write(ref writer);
            }
        }
    }

    /// <summary>Decodes a JSON literal from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded literal.</returns>
    public static JsonLiteral ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static JsonLiteral Read(ref CrdtReader reader)
    {
        var kind = (JsonLiteralKind)reader.ReadByte();
        if (kind == JsonLiteralKind.Primitive)
        {
            return PrimitiveValue(JsonPrimitive.Read(ref reader));
        }

        int count = reader.ReadCount();
        if (kind == JsonLiteralKind.Object)
        {
            var properties = new List<KeyValuePair<string, JsonLiteral>>(count);
            for (int i = 0; i < count; i++)
            {
                properties.Add(
                    new KeyValuePair<string, JsonLiteral>(reader.ReadString() ?? string.Empty, Read(ref reader)));
            }

            return Object(properties);
        }

        var items = new List<JsonLiteral>(count);
        for (int i = 0; i < count; i++)
        {
            items.Add(Read(ref reader));
        }

        return Array(items);
    }

    /// <inheritdoc/>
    public bool Equals(JsonLiteral? other)
    {
        if (other is null || Kind != other.Kind || !_primitive.Equals(other._primitive)
            || _properties.Count != other._properties.Count || _items.Count != other._items.Count)
        {
            return false;
        }

        for (int i = 0; i < _properties.Count; i++)
        {
            if (!string.Equals(_properties[i].Key, other._properties[i].Key, StringComparison.Ordinal)
                || !_properties[i].Value.Equals(other._properties[i].Value))
            {
                return false;
            }
        }

        for (int i = 0; i < _items.Count; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as JsonLiteral);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = HashCode.Combine(Kind, _primitive);
        foreach (KeyValuePair<string, JsonLiteral> property in _properties)
        {
            hash = HashCode.Combine(hash, property.Key, property.Value);
        }

        foreach (JsonLiteral item in _items)
        {
            hash = HashCode.Combine(hash, item);
        }

        return hash;
    }
}

/// <summary>Describes an idempotent operation over a <see cref="JsonCrdt"/> document.</summary>
public sealed class JsonOperation : IEquatable<JsonOperation>, IBinaryWritable
{
    private readonly IReadOnlyList<JsonPathSegment> _path;
    private readonly IReadOnlyList<Dot> _removedDots;

    private JsonOperation(
        JsonOperationKind kind,
        Dot dot,
        Timestamp timestamp,
        IReadOnlyList<JsonPathSegment> path,
        string key,
        Dot elementId,
        Dot afterElementId,
        JsonLiteral? value,
        JsonPrimitive primitive,
        IReadOnlyList<Dot> removedDots)
    {
        Kind = kind;
        Dot = dot;
        Timestamp = timestamp;
        _path = path;
        Key = key;
        ElementId = elementId;
        AfterElementId = afterElementId;
        Value = value;
        Primitive = primitive;
        _removedDots = removedDots;
    }

    /// <summary>Gets the operation kind.</summary>
    public JsonOperationKind Kind { get; }

    /// <summary>Gets the unique operation dot.</summary>
    public Dot Dot { get; }

    /// <summary>Gets the operation timestamp.</summary>
    public Timestamp Timestamp { get; }

    /// <summary>Gets the path to the target node.</summary>
    public IReadOnlyList<JsonPathSegment> Path => _path;

    /// <summary>Gets the map key for key operations.</summary>
    public string Key { get; }

    /// <summary>Gets the list element id inserted or deleted by list operations.</summary>
    public Dot ElementId { get; }

    /// <summary>Gets the list element id after which an insert occurs, or <see langword="default"/> for head.</summary>
    public Dot AfterElementId { get; }

    /// <summary>Gets the JSON literal for set and insert operations.</summary>
    public JsonLiteral? Value { get; }

    /// <summary>Gets the primitive value for assign operations.</summary>
    public JsonPrimitive Primitive { get; }

    /// <summary>Gets map assignment dots observed by a delete-key operation.</summary>
    public IReadOnlyList<Dot> RemovedDots => _removedDots;

    /// <summary>Creates a set-key operation.</summary>
    /// <param name="dot">The operation dot.</param>
    /// <param name="timestamp">The operation timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The JSON literal to assign.</param>
    /// <returns>The operation.</returns>
    public static JsonOperation SetKey(
        Dot dot,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        string key,
        JsonLiteral value)
    {
        Throw.IfNull(path);
        Throw.IfNull(key);
        Throw.IfNull(value);
        return new JsonOperation(
            JsonOperationKind.SetKey,
            dot,
            timestamp,
            new List<JsonPathSegment>(path),
            key,
            default,
            default,
            value,
            JsonPrimitive.Null,
            Array.Empty<Dot>());
    }

    /// <summary>Creates an insert-after operation.</summary>
    /// <param name="dot">The operation dot and inserted element id.</param>
    /// <param name="timestamp">The operation timestamp.</param>
    /// <param name="path">The target list path.</param>
    /// <param name="afterElementId">The predecessor element id, or default for head.</param>
    /// <param name="value">The JSON literal to insert.</param>
    /// <returns>The operation.</returns>
    public static JsonOperation InsertAfter(
        Dot dot,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        Dot afterElementId,
        JsonLiteral value)
    {
        Throw.IfNull(path);
        Throw.IfNull(value);
        return new JsonOperation(
            JsonOperationKind.InsertAfter,
            dot,
            timestamp,
            new List<JsonPathSegment>(path),
            string.Empty,
            dot,
            afterElementId,
            value,
            JsonPrimitive.Null,
            Array.Empty<Dot>());
    }

    /// <summary>Creates a delete-key operation.</summary>
    /// <param name="dot">The operation dot.</param>
    /// <param name="timestamp">The operation timestamp.</param>
    /// <param name="path">The target map path.</param>
    /// <param name="key">The key to delete.</param>
    /// <param name="removedDots">The observed assignment dots to remove.</param>
    /// <returns>The operation.</returns>
    public static JsonOperation DeleteKey(
        Dot dot,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        string key,
        IEnumerable<Dot> removedDots)
    {
        Throw.IfNull(path);
        Throw.IfNull(key);
        Throw.IfNull(removedDots);
        var dots = new List<Dot>(removedDots);
        dots.Sort();
        return new JsonOperation(
            JsonOperationKind.DeleteKey,
            dot,
            timestamp,
            new List<JsonPathSegment>(path),
            key,
            default,
            default,
            null,
            JsonPrimitive.Null,
            dots);
    }

    /// <summary>Creates a delete-index operation.</summary>
    /// <param name="dot">The operation dot.</param>
    /// <param name="timestamp">The operation timestamp.</param>
    /// <param name="path">The target list path.</param>
    /// <param name="elementId">The stable element id to delete.</param>
    /// <returns>The operation.</returns>
    public static JsonOperation DeleteIndex(
        Dot dot,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        Dot elementId)
    {
        Throw.IfNull(path);
        return new JsonOperation(
            JsonOperationKind.DeleteIndex,
            dot,
            timestamp,
            new List<JsonPathSegment>(path),
            string.Empty,
            elementId,
            default,
            null,
            JsonPrimitive.Null,
            Array.Empty<Dot>());
    }

    /// <summary>Creates a register assignment operation.</summary>
    /// <param name="dot">The operation dot.</param>
    /// <param name="timestamp">The operation timestamp.</param>
    /// <param name="path">The target register path.</param>
    /// <param name="primitive">The assigned primitive.</param>
    /// <returns>The operation.</returns>
    public static JsonOperation Assign(
        Dot dot,
        Timestamp timestamp,
        IEnumerable<JsonPathSegment> path,
        JsonPrimitive primitive)
    {
        Throw.IfNull(path);
        return new JsonOperation(
            JsonOperationKind.Assign,
            dot,
            timestamp,
            new List<JsonPathSegment>(path),
            string.Empty,
            default,
            default,
            null,
            primitive,
            Array.Empty<Dot>());
    }

    /// <summary>Serializes this operation to a new byte array.</summary>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray() => CrdtBinary.ToByteArray(this);

    /// <inheritdoc/>
    public void Write(ref CrdtWriter writer)
    {
        writer.WriteByte((byte)Kind);
        writer.WriteDot(Dot);
        writer.WriteTimestamp(Timestamp);
        writer.WriteVarUInt64((ulong)_path.Count);
        foreach (JsonPathSegment segment in _path)
        {
            segment.Write(ref writer);
        }

        writer.WriteString(Key);
        writer.WriteDot(ElementId);
        writer.WriteDot(AfterElementId);
        writer.WriteBool(Value is not null);
        Value?.Write(ref writer);
        Primitive.Write(ref writer);
        writer.WriteVarUInt64((ulong)_removedDots.Count);
        foreach (Dot removedDot in _removedDots)
        {
            writer.WriteDot(removedDot);
        }
    }

    /// <summary>Decodes an operation from the binary format.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static JsonOperation ReadFrom(ReadOnlySpan<byte> data, CrdtReaderOptions? options = null)
    {
        var reader = new CrdtReader(data, options);
        return Read(ref reader);
    }

    internal static JsonOperation Read(ref CrdtReader reader)
    {
        var kind = (JsonOperationKind)reader.ReadByte();
        Dot dot = reader.ReadDot();
        Timestamp timestamp = reader.ReadTimestamp();
        int pathCount = reader.ReadCount();
        var path = new List<JsonPathSegment>(pathCount);
        for (int i = 0; i < pathCount; i++)
        {
            path.Add(JsonPathSegment.Read(ref reader));
        }

        string key = reader.ReadString() ?? string.Empty;
        Dot elementId = reader.ReadDot();
        Dot afterElementId = reader.ReadDot();
        JsonLiteral? value = reader.ReadBool() ? JsonLiteral.Read(ref reader) : null;
        JsonPrimitive primitive = JsonPrimitive.Read(ref reader);
        int removedCount = reader.ReadCount();
        var removed = new List<Dot>(removedCount);
        for (int i = 0; i < removedCount; i++)
        {
            removed.Add(reader.ReadDot());
        }

        return new JsonOperation(kind, dot, timestamp, path, key, elementId, afterElementId, value, primitive, removed);
    }

    /// <inheritdoc/>
    public bool Equals(JsonOperation? other)
    {
        if (other is null || Kind != other.Kind || Dot != other.Dot || Timestamp != other.Timestamp
            || !string.Equals(Key, other.Key, StringComparison.Ordinal) || ElementId != other.ElementId
            || AfterElementId != other.AfterElementId
            || !EqualityComparer<JsonLiteral?>.Default.Equals(Value, other.Value)
            || !Primitive.Equals(other.Primitive) || _path.Count != other._path.Count
            || _removedDots.Count != other._removedDots.Count)
        {
            return false;
        }

        for (int i = 0; i < _path.Count; i++)
        {
            if (!_path[i].Equals(other._path[i]))
            {
                return false;
            }
        }

        for (int i = 0; i < _removedDots.Count; i++)
        {
            if (_removedDots[i] != other._removedDots[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as JsonOperation);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Kind, Dot, Timestamp, Key, ElementId, AfterElementId);
}
