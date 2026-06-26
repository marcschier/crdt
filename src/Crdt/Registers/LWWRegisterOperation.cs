// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Crdt;

/// <summary>
/// The operation produced by <see cref="LWWRegister{T}"/> when its value is assigned.
/// Applying it keeps the value with the greatest timestamp and is idempotent.
/// </summary>
/// <typeparam name="T">The assigned value type.</typeparam>
public readonly struct LWWRegisterOperation<T>
{
    /// <summary>Initializes a new <see cref="LWWRegisterOperation{T}"/>.</summary>
    /// <param name="value">The assigned value.</param>
    /// <param name="timestamp">The assignment timestamp.</param>
    public LWWRegisterOperation(T value, Timestamp timestamp)
    {
        HasValue = true;
        Value = value;
        Timestamp = timestamp;
    }

    private LWWRegisterOperation(bool hasValue, T? value, Timestamp timestamp)
    {
        HasValue = hasValue;
        Value = value;
        Timestamp = timestamp;
    }

    /// <summary>Gets a value indicating whether the operation carries an assigned value.</summary>
    public bool HasValue { get; }

    /// <summary>Gets the assigned value, or <see langword="default"/> when empty.</summary>
    public T? Value { get; }

    /// <summary>Gets the assignment timestamp.</summary>
    public Timestamp Timestamp { get; }

    /// <summary>Serializes this operation using the supplied value serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The value serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        Write(ref writer, serializer);
    }

    /// <summary>Serializes this operation to a new byte array using <paramref name="serializer"/>.</summary>
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
            serializer.Write(ref writer, Value!);
            writer.WriteTimestamp(Timestamp);
        }
    }

    /// <summary>Decodes an operation using the supplied value serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The value serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static LWWRegisterOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return Read(ref reader, serializer);
    }

    internal static LWWRegisterOperation<T> Read(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        bool hasValue = reader.ReadBool();
        if (!hasValue)
        {
            return new LWWRegisterOperation<T>(false, default, Timestamp.MinValue);
        }

        T value = serializer.Read(ref reader);
        Timestamp timestamp = reader.ReadTimestamp();
        return new LWWRegisterOperation<T>(true, value, timestamp);
    }
}
