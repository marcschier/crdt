// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Crdt;

/// <summary>
/// The operation produced by <see cref="MVRegister{T}"/> when a value is assigned. It carries
/// the new dot, value, and causal context needed to remove values observed by the assignment.
/// </summary>
/// <typeparam name="T">The assigned value type.</typeparam>
public readonly struct MVRegisterOperation<T>
    where T : notnull
{
    private readonly DotContext? _context;

    internal MVRegisterOperation(Dot dot, T value, DotContext context)
    {
        Dot = dot;
        Value = value;
        _context = context;
    }

    /// <summary>Gets the dot that identifies the assignment.</summary>
    public Dot Dot { get; }

    /// <summary>Gets the assigned value.</summary>
    public T? Value { get; }

    internal DotContext Context => _context ?? new DotContext();

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
        writer.WriteDot(Dot);
        serializer.Write(ref writer, Value!);
        Context.Write(ref writer);
    }

    /// <summary>Decodes an operation using the supplied value serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The value serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static MVRegisterOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return Read(ref reader, serializer);
    }

    internal static MVRegisterOperation<T> Read(ref CrdtReader reader, ICrdtValueSerializer<T> serializer)
    {
        Dot dot = reader.ReadDot();
        T value = serializer.Read(ref reader);
        DotContext context = DotContext.Read(ref reader);
        return new MVRegisterOperation<T>(dot, value, context);
    }
}
