// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Crdt;

/// <summary>
/// The operation broadcast by a <see cref="GSet{T}"/> when an element is added. Applying it is
/// idempotent and commutative (a set insertion), so no causal metadata is required.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public readonly struct GSetOperation<T>
    where T : notnull
{
    /// <summary>Initializes a new <see cref="GSetOperation{T}"/>.</summary>
    /// <param name="element">The element that was added.</param>
    public GSetOperation(T element) => Element = element;

    /// <summary>Gets the added element.</summary>
    public T Element { get; }

    /// <summary>Serializes this operation using the supplied element serializer.</summary>
    /// <param name="output">The destination buffer writer.</param>
    /// <param name="serializer">The element serializer.</param>
    public void WriteTo(IBufferWriter<byte> output, ICrdtValueSerializer<T> serializer)
    {
        Throw.IfNull(serializer);
        var writer = new CrdtWriter(output);
        serializer.Write(ref writer, Element);
    }

    /// <summary>Decodes an operation using the supplied element serializer.</summary>
    /// <param name="data">The encoded bytes.</param>
    /// <param name="serializer">The element serializer.</param>
    /// <param name="options">Optional decoding limits.</param>
    /// <returns>The decoded operation.</returns>
    public static GSetOperation<T> ReadFrom(
        ReadOnlySpan<byte> data,
        ICrdtValueSerializer<T> serializer,
        CrdtReaderOptions? options = null)
    {
        Throw.IfNull(serializer);
        var reader = new CrdtReader(data, options);
        return new GSetOperation<T>(serializer.Read(ref reader));
    }
}
