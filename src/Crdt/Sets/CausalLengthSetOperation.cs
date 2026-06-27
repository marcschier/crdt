// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Crdt;

/// <summary>
/// Describes an idempotent causal-length set operation by carrying an element and its observed
/// monotonically increasing causal length.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct CausalLengthSetOperation<T>
    where T : notnull
{
    /// <summary>Initializes a new <see cref="CausalLengthSetOperation{T}"/>.</summary>
    /// <param name="element">The element whose length changed.</param>
    /// <param name="length">The new causal length for the element.</param>
    public CausalLengthSetOperation(T element, ulong length)
    {
        Element = element;
        Length = length;
    }

    /// <summary>Gets the element whose length changed.</summary>
    public T Element { get; }

    /// <summary>Gets the operation's causal length.</summary>
    public ulong Length { get; }

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        serializer.Write(ref writer, Element);
        writer.WriteVarUInt64(Length);
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static CausalLengthSetOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        T element = serializer.Read(ref reader);
        return new CausalLengthSetOperation<T>(element, reader.ReadVarUInt64());
    }
}
