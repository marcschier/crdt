// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Transport;

/// <summary>Represents a decoded transport frame.</summary>
/// <param name="MessageType">The decoded message kind.</param>
/// <param name="Payload">The decoded payload bytes.</param>
public readonly record struct DecodedFrame(MessageType MessageType, ReadOnlyMemory<byte> Payload);

/// <summary>Encodes and decodes CRDT transport frames.</summary>
public static class FrameCodec
{
    /// <summary>The default maximum frame body length in bytes.</summary>
    public const int DefaultMaxFrameLength = 16 * 1024 * 1024;

    /// <summary>Encodes a message type and payload as a length-prefixed frame.</summary>
    /// <param name="messageType">The message kind to encode.</param>
    /// <param name="payload">The payload bytes.</param>
    /// <param name="maxFrameLength">The maximum allowed frame body length.</param>
    /// <returns>The encoded frame, including the varint length prefix.</returns>
    public static byte[] Encode(
        MessageType messageType,
        ReadOnlySpan<byte> payload,
        int maxFrameLength = DefaultMaxFrameLength)
    {
        ValidateMessageType(messageType);
        int bodyLength = checked(payload.Length + 1);
        ValidateBodyLength(bodyLength, maxFrameLength);

        int prefixLength = GetVarUInt64Length((ulong)bodyLength);
        var frame = new byte[prefixLength + bodyLength];
        int position = WriteVarUInt64(frame, (ulong)bodyLength);
        frame[position] = (byte)messageType;
        payload.CopyTo(frame.AsSpan(position + 1));
        return frame;
    }

    /// <summary>Decodes a complete length-prefixed frame.</summary>
    /// <param name="frame">The encoded frame, including the varint length prefix.</param>
    /// <param name="maxFrameLength">The maximum allowed frame body length.</param>
    /// <returns>The decoded frame.</returns>
    public static DecodedFrame Decode(
        ReadOnlyMemory<byte> frame,
        int maxFrameLength = DefaultMaxFrameLength)
    {
        ReadOnlySpan<byte> span = frame.Span;
        ulong bodyLength = ReadVarUInt64(span, out int prefixLength);
        ValidateBodyLength(bodyLength, maxFrameLength);

        int length = checked((int)bodyLength);
        if (span.Length - prefixLength != length)
        {
            throw new InvalidDataException("Frame length does not match the encoded body length.");
        }

        if (length == 0)
        {
            throw new InvalidDataException("Frame body must include a message type byte.");
        }

        var messageType = (MessageType)span[prefixLength];
        ValidateMessageType(messageType);
        return new DecodedFrame(messageType, frame.Slice(prefixLength + 1, length - 1));
    }

    /// <summary>Attempts to decode a complete length-prefixed frame.</summary>
    /// <param name="frame">The encoded frame, including the varint length prefix.</param>
    /// <param name="decoded">The decoded frame when decoding succeeds.</param>
    /// <param name="maxFrameLength">The maximum allowed frame body length.</param>
    /// <returns><see langword="true"/> when decoding succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryDecode(
        ReadOnlyMemory<byte> frame,
        out DecodedFrame decoded,
        int maxFrameLength = DefaultMaxFrameLength)
    {
        try
        {
            decoded = Decode(frame, maxFrameLength);
            return true;
        }
        catch (InvalidDataException)
        {
            decoded = default;
            return false;
        }
        catch (OverflowException)
        {
            decoded = default;
            return false;
        }
    }

    internal static int GetVarUInt64Length(ulong value)
    {
        int length = 1;
        while (value >= 0x80)
        {
            length++;
            value >>= 7;
        }

        return length;
    }

    internal static int WriteVarUInt64(Span<byte> destination, ulong value)
    {
        int i = 0;
        while (value >= 0x80)
        {
            destination[i++] = (byte)(value | 0x80);
            value >>= 7;
        }

        destination[i++] = (byte)value;
        return i;
    }

    internal static ulong ReadVarUInt64(ReadOnlySpan<byte> source, out int bytesRead)
    {
        ulong result = 0;
        int shift = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (shift >= 64)
            {
                throw new InvalidDataException("Varint length prefix is too long.");
            }

            byte value = source[i];
            result |= (ulong)(value & 0x7F) << shift;
            if ((value & 0x80) == 0)
            {
                bytesRead = i + 1;
                return result;
            }

            shift += 7;
        }

        throw new InvalidDataException("Frame ended before the length prefix completed.");
    }

    internal static void ValidateBodyLength(ulong bodyLength, int maxFrameLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameLength);

        if (bodyLength == 0 || bodyLength > (ulong)maxFrameLength || bodyLength > int.MaxValue)
        {
            throw new InvalidDataException("Frame body length exceeds the configured maximum.");
        }
    }

    private static void ValidateBodyLength(int bodyLength, int maxFrameLength) =>
        ValidateBodyLength((ulong)bodyLength, maxFrameLength);

    private static void ValidateMessageType(MessageType messageType)
    {
        if (messageType is < MessageType.State or > MessageType.Ack)
        {
            throw new InvalidDataException("Unknown transport message type.");
        }
    }
}
