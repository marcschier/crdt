// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;

namespace Crdt;

/// <summary>
/// Implemented by CRDT states, deltas, and operations that can serialize themselves to the
/// compact CRDT binary format. The decoding counterpart is a per-type static
/// <c>ReadFrom(ReadOnlySpan&lt;byte&gt;, CrdtReaderOptions?)</c> method (a static-abstract
/// interface member cannot be expressed on every supported target framework).
/// </summary>
public interface IBinaryWritable
{
    /// <summary>Writes this value to <paramref name="writer"/> in canonical, deterministic order.</summary>
    /// <param name="writer">The destination writer.</param>
    void Write(ref CrdtWriter writer);
}

/// <summary>
/// Convenience helpers for the CRDT binary format: materialising an
/// <see cref="IBinaryWritable"/> to a byte array or an arbitrary
/// <see cref="IBufferWriter{T}"/>.
/// </summary>
public static class CrdtBinary
{
    /// <summary>The current CRDT binary format version, for payloads that embed a version tag.</summary>
    public const byte FormatVersion = 1;

    /// <summary>Serializes <paramref name="value"/> to a freshly-allocated byte array.</summary>
    /// <typeparam name="T">The writable type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] ToByteArray<T>(this T value)
        where T : IBinaryWritable
    {
        Throw.IfNull(value);
        using var buffer = new PooledBufferWriter();
        var writer = new CrdtWriter(buffer);
        value.Write(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Serializes <paramref name="value"/> into the supplied buffer sink.</summary>
    /// <typeparam name="T">The writable type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="output">The destination buffer writer.</param>
    public static void WriteTo<T>(this T value, IBufferWriter<byte> output)
        where T : IBinaryWritable
    {
        Throw.IfNull(value);
        var writer = new CrdtWriter(output);
        value.Write(ref writer);
    }
}
