// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace Crdt;

/// <summary>
/// A forward-only binary reader over a <see cref="ReadOnlySpan{T}"/>, the decoding
/// counterpart of <see cref="CrdtWriter"/>. Every read is bounds-checked, and
/// collection/string lengths are validated against <see cref="CrdtReaderOptions"/> so that
/// malformed or hostile input fails fast instead of allocating unboundedly.
/// </summary>
public ref struct CrdtReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private readonly CrdtReaderOptions _options;
    private int _position;

    /// <summary>Initializes a new <see cref="CrdtReader"/>.</summary>
    /// <param name="buffer">The encoded bytes to read from.</param>
    /// <param name="options">Decoding limits; defaults to <see cref="CrdtReaderOptions.Default"/>.</param>
    public CrdtReader(ReadOnlySpan<byte> buffer, CrdtReaderOptions? options = null)
    {
        _buffer = buffer;
        _options = options ?? CrdtReaderOptions.Default;
        _position = 0;
    }

    /// <summary>Gets the current read offset.</summary>
    public readonly int Position => _position;

    /// <summary>Gets a value indicating whether all bytes have been consumed.</summary>
    public readonly bool End => _position >= _buffer.Length;

    /// <summary>Gets the decoding options in effect.</summary>
    public readonly CrdtReaderOptions Options => _options;

    /// <summary>Reads a single raw byte.</summary>
    /// <returns>The byte read.</returns>
    public byte ReadByte()
    {
        if (_position >= _buffer.Length)
        {
            Throw.InvalidData<byte>("Unexpected end of CRDT binary stream.");
        }

        return _buffer[_position++];
    }

    /// <summary>Reads a boolean encoded as one byte.</summary>
    /// <returns>The boolean read.</returns>
    public bool ReadBool() => ReadByte() != 0;

    /// <summary>Reads <paramref name="length"/> raw bytes.</summary>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>A span over the bytes (a view into the source buffer).</returns>
    public ReadOnlySpan<byte> ReadRaw(int length)
    {
        if (length < 0 || _position + length > _buffer.Length)
        {
            Throw.InvalidData<byte>("Unexpected end of CRDT binary stream.");
        }

        ReadOnlySpan<byte> slice = _buffer.Slice(_position, length);
        _position += length;
        return slice;
    }

    /// <summary>Reads a 64-bit unsigned LEB128 varint.</summary>
    /// <returns>The decoded value.</returns>
    public ulong ReadVarUInt64()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            if (shift >= 64)
            {
                Throw.InvalidData<ulong>("Varint is too long.");
            }

            byte b = ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
        }
    }

    /// <summary>Reads a 32-bit unsigned LEB128 varint, validating range.</summary>
    /// <returns>The decoded value.</returns>
    public uint ReadVarUInt32()
    {
        ulong value = ReadVarUInt64();
        if (value > uint.MaxValue)
        {
            Throw.InvalidData<uint>("Varint exceeds 32-bit range.");
        }

        return (uint)value;
    }

    /// <summary>Reads a zig-zag plus LEB128 encoded 64-bit signed value.</summary>
    /// <returns>The decoded value.</returns>
    public long ReadVarInt64() => UnZigZag(ReadVarUInt64());

    /// <summary>Reads a fixed-width little-endian 64-bit unsigned value.</summary>
    /// <returns>The decoded value.</returns>
    public ulong ReadFixedUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadRaw(8));

    /// <summary>Reads a <see cref="Guid"/> from 16 raw bytes.</summary>
    /// <returns>The decoded value.</returns>
    public Guid ReadGuid() => new(ReadRaw(16));

    /// <summary>Reads a <see cref="ReplicaId"/>.</summary>
    /// <returns>The decoded value.</returns>
    public ReplicaId ReadReplicaId() => new(ReadGuid());

    /// <summary>Reads a <see cref="Dot"/>.</summary>
    /// <returns>The decoded value.</returns>
    public Dot ReadDot() => new(ReadReplicaId(), ReadVarUInt64());

    /// <summary>Reads a <see cref="Timestamp"/>.</summary>
    /// <returns>The decoded value.</returns>
    public Timestamp ReadTimestamp() => new(ReadVarInt64(), ReadVarUInt64(), ReadReplicaId());

    /// <summary>Reads a length-prefixed UTF-8 string (see <see cref="CrdtWriter.WriteString"/>).</summary>
    /// <returns>The decoded string, or <see langword="null"/>.</returns>
    public string? ReadString()
    {
        ulong prefix = ReadVarUInt64();
        if (prefix == 0)
        {
            return null;
        }

        ulong byteCount = prefix - 1UL;
        if (byteCount > (ulong)_options.MaxStringBytes)
        {
            Throw.InvalidData<string>("Encoded string exceeds the configured maximum length.");
        }

        if (byteCount == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(ReadRaw((int)byteCount));
    }

    /// <summary>
    /// Reads a collection length prefix and validates it against
    /// <see cref="CrdtReaderOptions.MaxCollectionCount"/>.
    /// </summary>
    /// <returns>The element count.</returns>
    public int ReadCount()
    {
        ulong count = ReadVarUInt64();
        if (count > (ulong)_options.MaxCollectionCount)
        {
            Throw.InvalidData<int>("Encoded collection exceeds the configured maximum element count.");
        }

        return (int)count;
    }

    private static long UnZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);
}
