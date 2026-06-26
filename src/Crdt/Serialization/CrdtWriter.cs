// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Crdt;

/// <summary>
/// A forward-only, allocation-light binary writer over an <see cref="IBufferWriter{T}"/>.
/// Encodes the primitive vocabulary shared by every CRDT (varints, dots, timestamps,
/// strings) using little-endian byte order and LEB128 variable-length integers. Output is
/// deterministic: callers are responsible for writing collections in canonical order.
/// </summary>
public ref struct CrdtWriter
{
    private readonly IBufferWriter<byte> _output;

    /// <summary>Initializes a new <see cref="CrdtWriter"/> over the given buffer sink.</summary>
    /// <param name="output">The destination buffer writer.</param>
    public CrdtWriter(IBufferWriter<byte> output)
    {
        Throw.IfNull(output);
        _output = output;
    }

    /// <summary>Writes a single raw byte.</summary>
    /// <param name="value">The byte to write.</param>
    public void WriteByte(byte value)
    {
        Span<byte> span = _output.GetSpan(1);
        span[0] = value;
        _output.Advance(1);
    }

    /// <summary>Writes a boolean as one byte (0 or 1).</summary>
    /// <param name="value">The value to write.</param>
    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Writes a span of raw bytes verbatim.</summary>
    /// <param name="bytes">The bytes to write.</param>
    public void WriteRaw(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        Span<byte> span = _output.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _output.Advance(bytes.Length);
    }

    /// <summary>Writes a 64-bit unsigned value using LEB128 variable-length encoding.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteVarUInt64(ulong value)
    {
        // At most 10 bytes for a 64-bit varint.
        Span<byte> span = _output.GetSpan(10);
        int i = 0;
        while (value >= 0x80)
        {
            span[i++] = (byte)(value | 0x80);
            value >>= 7;
        }

        span[i++] = (byte)value;
        _output.Advance(i);
    }

    /// <summary>Writes a 32-bit unsigned value using LEB128 variable-length encoding.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteVarUInt32(uint value) => WriteVarUInt64(value);

    /// <summary>Writes a 64-bit signed value using zig-zag plus LEB128 encoding.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteVarInt64(long value) => WriteVarUInt64(ZigZag(value));

    /// <summary>Writes a fixed-width little-endian 64-bit unsigned value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteFixedUInt64(ulong value)
    {
        Span<byte> span = _output.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _output.Advance(8);
    }

    /// <summary>Writes a <see cref="Guid"/> as 16 raw bytes.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteGuid(Guid value)
    {
        Span<byte> span = _output.GetSpan(16);
        SpanCompat.WriteGuidBytes(value, span);
        _output.Advance(16);
    }

    /// <summary>Writes a <see cref="ReplicaId"/>.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteReplicaId(ReplicaId value) => WriteGuid(value.Value);

    /// <summary>Writes a <see cref="Dot"/> (replica id followed by a varint sequence).</summary>
    /// <param name="value">The value to write.</param>
    public void WriteDot(Dot value)
    {
        WriteReplicaId(value.Replica);
        WriteVarUInt64(value.Sequence);
    }

    /// <summary>Writes a <see cref="Timestamp"/>.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteTimestamp(Timestamp value)
    {
        WriteVarInt64(value.WallClock);
        WriteVarUInt64(value.Counter);
        WriteReplicaId(value.Origin);
    }

    /// <summary>
    /// Writes a length-prefixed UTF-8 string. A <see langword="null"/> string is encoded as
    /// length 0; an empty string as length 1 (with no payload bytes).
    /// </summary>
    /// <param name="value">The string to write, which may be <see langword="null"/>.</param>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteVarUInt64(0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value.AsSpan());
        WriteVarUInt64((ulong)byteCount + 1UL);
        if (byteCount == 0)
        {
            return;
        }

        Span<byte> span = _output.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(value.AsSpan(), span);
        _output.Advance(written);
    }

    private static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));
}
