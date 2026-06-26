// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Crdt;

/// <summary>
/// A last-writer-wins register: a state-based, delta-state, and operation-based CRDT that
/// stores one value and keeps the assignment with the greatest <see cref="Timestamp"/>.
/// </summary>
/// <typeparam name="T">The stored value type.</typeparam>
/// <remarks>Mutable and not thread-safe.</remarks>
public sealed class LWWRegister<T> :
    IConvergent<LWWRegister<T>>,
    IDeltaConvergent<LWWRegister<T>, LWWRegister<T>>,
    IOperationConvergent<LWWRegisterOperation<T>>,
    IEquatable<LWWRegister<T>>
{
    private T? _value;
    private Timestamp _timestamp;
    private LWWRegister<T>? _delta;

    /// <summary>Initializes an empty last-writer-wins register.</summary>
    public LWWRegister()
    {
    }

    private LWWRegister(bool hasValue, T? value, Timestamp timestamp)
    {
        HasValue = hasValue;
        _value = value;
        _timestamp = timestamp;
    }

    /// <summary>Gets a value indicating whether the register currently contains a value.</summary>
    public bool HasValue { get; private set; }

    /// <summary>Gets the current value, or <see langword="default"/> when the register is empty.</summary>
    public T? Value => _value;

    /// <summary>Gets the timestamp of the current value, or <see cref="Timestamp.MinValue"/> when empty.</summary>
    public Timestamp Timestamp => HasValue ? _timestamp : Timestamp.MinValue;

    /// <summary>Attempts to get the current value.</summary>
    /// <param name="value">The current value when the register is not empty.</param>
    /// <returns><see langword="true"/> when a value is present; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return HasValue;
    }

    /// <summary>Assigns <paramref name="value"/> at <paramref name="timestamp"/>.</summary>
    /// <param name="value">The value to assign.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    /// <returns>The operation describing the assignment.</returns>
    public LWWRegisterOperation<T> Set(T value, Timestamp timestamp)
    {
        var operation = new LWWRegisterOperation<T>(value, timestamp);
        Apply(operation);
        _delta = Clone();
        return operation;
    }

    /// <summary>Assigns <paramref name="value"/> using a fresh timestamp from <paramref name="clock"/>.</summary>
    /// <param name="value">The value to assign.</param>
    /// <param name="clock">The clock used to stamp the assignment.</param>
    /// <returns>The operation describing the assignment.</returns>
    public LWWRegisterOperation<T> Set(T value, IClock clock)
    {
        Throw.IfNull(clock);
        return Set(value, clock.Now());
    }

    /// <inheritdoc/>
    public void Merge(LWWRegister<T> other)
    {
        Throw.IfNull(other);
        if (ShouldAccept(other.HasValue, other._timestamp))
        {
            HasValue = true;
            _value = other._value;
            _timestamp = other._timestamp;
        }
    }

    /// <inheritdoc/>
    public CrdtOrder Compare(LWWRegister<T> other)
    {
        Throw.IfNull(other);
        if (!HasValue && !other.HasValue)
        {
            return CrdtOrder.Equal;
        }

        if (!HasValue)
        {
            return CrdtOrder.Less;
        }

        if (!other.HasValue)
        {
            return CrdtOrder.Greater;
        }

        int order = _timestamp.CompareTo(other._timestamp);
        return order switch
        {
            < 0 => CrdtOrder.Less,
            > 0 => CrdtOrder.Greater,
            _ => CrdtOrder.Equal,
        };
    }

    /// <inheritdoc/>
    public LWWRegister<T> Clone() => new(HasValue, _value, _timestamp);

    /// <inheritdoc/>
    public bool TryExtractDelta([MaybeNullWhen(false)] out LWWRegister<T> delta)
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
    public void MergeDelta(LWWRegister<T> delta)
    {
        Throw.IfNull(delta);
        Merge(delta);
    }

    /// <inheritdoc/>
    public bool Apply(LWWRegisterOperation<T> operation)
    {
        if (!ShouldAccept(operation.HasValue, operation.Timestamp))
        {
            return false;
        }

        HasValue = true;
        _value = operation.Value;
        _timestamp = operation.Timestamp;
        return true;
    }

    /// <summary>Serializes the register to the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The value serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        Write(ref writer, serializer);
    }

    /// <summary>Serializes the register to a new byte array using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The value serializer.</param>
    /// <returns>The encoded bytes.</returns>
    public byte[] ToByteArray(ICrdtValueSerializer<T> serializer)
    {
        using var buffer = new PooledBufferWriter();
        WriteTo(buffer, serializer);
        return buffer.WrittenSpan.ToArray();
    }

    internal void Write(ref CrdtWriter writer, ICrdtValueSerializer<T> serializer)
    {
        writer.WriteBool(HasValue);
        if (HasValue)
        {
            serializer.Write(ref writer, _value!);
            writer.WriteTimestamp(_timestamp);
        }
    }

    /// <summary>Decodes a register from the binary format using <paramref name="serializer"/>.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The value serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded register.</returns>
    public static LWWRegister<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return Read(ref reader, serializer);
    }

    internal static LWWRegister<T> Read(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        bool hasValue = reader.ReadBool();
        if (!hasValue)
        {
            return new LWWRegister<T>();
        }

        T value = serializer.Read(ref reader);
        Timestamp timestamp = reader.ReadTimestamp();
        return new LWWRegister<T>(true, value, timestamp);
    }

    /// <summary>Serializes the register to JSON using <paramref name="serializer"/>.</summary>
    /// <param name="serializer">The value serializer.</param>
    /// <returns>The JSON string.</returns>
    public string ToJson(ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        using var buffer = new PooledBufferWriter();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("hasValue", HasValue);
            if (HasValue)
            {
                writer.WritePropertyName("value");
                serializer.WriteJson(writer, _value!);
                WriteTimestampJson(writer, _timestamp);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Deserializes a register from JSON using <paramref name="serializer"/>.</summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="serializer">The value serializer.</param>
    /// <returns>The decoded register.</returns>
    public static LWWRegister<T> FromJson(string json, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(json);
        Throw.IfNull(serializer);
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        bool hasValue = false;
        T? value = default;
        Timestamp timestamp = Timestamp.MinValue;

        reader.Read();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? propertyName = reader.GetString();
            reader.Read();
            if (propertyName == "hasValue")
            {
                hasValue = reader.GetBoolean();
            }
            else if (propertyName == "value")
            {
                value = serializer.ReadJson(ref reader);
            }
            else if (propertyName == "timestamp")
            {
                timestamp = ReadTimestampJson(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        return hasValue ? new LWWRegister<T>(true, value, timestamp) : new LWWRegister<T>();
    }

    /// <inheritdoc/>
    public bool Equals(LWWRegister<T>? other)
    {
        if (other is null || HasValue != other.HasValue)
        {
            return false;
        }

        return !HasValue || (_timestamp.Equals(other._timestamp)
            && EqualityComparer<T>.Default.Equals(_value!, other._value!));
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as LWWRegister<T>);

    /// <inheritdoc/>
    public override int GetHashCode() => HasValue ? HashCode.Combine(_timestamp, _value) : 0;

    internal static void WriteTimestampJson(Utf8JsonWriter writer, Timestamp timestamp)
    {
        writer.WritePropertyName("timestamp");
        writer.WriteStartObject();
        writer.WriteNumber("wallClock", timestamp.WallClock);
        writer.WriteNumber("counter", timestamp.Counter);
        writer.WriteString("origin", timestamp.Origin.Value);
        writer.WriteEndObject();
    }

    internal static Timestamp ReadTimestampJson(ref Utf8JsonReader reader)
    {
        long wallClock = 0;
        ulong counter = 0UL;
        ReplicaId origin = ReplicaId.Empty;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string? propertyName = reader.GetString();
            reader.Read();
            if (propertyName == "wallClock")
            {
                wallClock = reader.GetInt64();
            }
            else if (propertyName == "counter")
            {
                counter = reader.GetUInt64();
            }
            else if (propertyName == "origin")
            {
                origin = new ReplicaId(reader.GetGuid());
            }
            else
            {
                reader.Skip();
            }
        }

        return new Timestamp(wallClock, counter, origin);
    }

    private bool ShouldAccept(bool hasValue, Timestamp timestamp) =>
        hasValue && (!HasValue || timestamp.CompareTo(_timestamp) > 0);
}
