// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;
using Crdt.Transport;

namespace Crdt.Consensus;

internal enum ConsensusEnvelopeKind : byte
{
    Heartbeat = 1,
    Proposal = 2,
    Ack = 3,
    Commit = 4,
}

internal readonly struct ConsensusEnvelope
{
    public ConsensusEnvelope(
        ConsensusEnvelopeKind kind,
        ReplicaId senderId,
        ReplicaId? recipientId,
        Guid messageId,
        ReadOnlyMemory<byte> payload)
    {
        Kind = kind;
        SenderId = senderId;
        RecipientId = recipientId;
        MessageId = messageId;
        Payload = payload;
    }

    public ConsensusEnvelopeKind Kind { get; }

    public ReplicaId SenderId { get; }

    public ReplicaId? RecipientId { get; }

    public Guid MessageId { get; }

    public ReadOnlyMemory<byte> Payload { get; }
}

internal static class ConsensusEnvelopeCodec
{
    private const byte Magic0 = (byte)'C';
    private const byte Magic1 = (byte)'C';
    private const byte Magic2 = (byte)'N';
    private const byte Magic3 = (byte)'S';
    private const byte Version = 1;
    private const byte HasRecipient = 1;
    private const int VersionOffset = 4;
    private const int KindOffset = 5;
    private const int FlagsOffset = 6;
    private const int SenderOffset = 7;
    private const int RecipientOffset = SenderOffset + 16;
    private const int MessageIdOffset = RecipientOffset + 16;
    private const int PayloadLengthOffset = MessageIdOffset + 16;
    private const int HeaderLength = PayloadLengthOffset + 4;

    public static byte[] Encode(
        ConsensusEnvelopeKind kind,
        ReplicaId senderId,
        ReplicaId? recipientId,
        Guid messageId,
        ReadOnlyMemory<byte> payload,
        int maxFrameLength)
    {
        int envelopeLength = checked(HeaderLength + payload.Length);
        var envelope = new byte[envelopeLength];
        envelope[0] = Magic0;
        envelope[1] = Magic1;
        envelope[2] = Magic2;
        envelope[3] = Magic3;
        envelope[VersionOffset] = Version;
        envelope[KindOffset] = (byte)kind;
        envelope[FlagsOffset] = recipientId.HasValue ? HasRecipient : (byte)0;

        WriteGuid(senderId.Value, envelope.AsSpan(SenderOffset, 16));
        if (recipientId.HasValue)
        {
            WriteGuid(recipientId.Value.Value, envelope.AsSpan(RecipientOffset, 16));
        }

        WriteGuid(messageId, envelope.AsSpan(MessageIdOffset, 16));
        BinaryPrimitives.WriteInt32BigEndian(envelope.AsSpan(PayloadLengthOffset, 4), payload.Length);
        payload.Span.CopyTo(envelope.AsSpan(HeaderLength));
        return FrameCodec.Encode(MessageType.Operation, envelope, maxFrameLength);
    }

    public static bool TryDecode(
        ReadOnlyMemory<byte> message,
        out ConsensusEnvelope envelope,
        int maxFrameLength)
    {
        envelope = default;
        if (!FrameCodec.TryDecode(message, out DecodedFrame decoded, maxFrameLength)
            || decoded.MessageType != MessageType.Operation)
        {
            return false;
        }

        ReadOnlyMemory<byte> bodyMemory = decoded.Payload;
        ReadOnlySpan<byte> body = bodyMemory.Span;
        if (body.Length < HeaderLength
            || body[0] != Magic0
            || body[1] != Magic1
            || body[2] != Magic2
            || body[3] != Magic3
            || body[VersionOffset] != Version)
        {
            return false;
        }

        var kind = (ConsensusEnvelopeKind)body[KindOffset];
        if (kind is < ConsensusEnvelopeKind.Heartbeat or > ConsensusEnvelopeKind.Commit)
        {
            return false;
        }

        byte flags = body[FlagsOffset];
        if ((flags & ~HasRecipient) != 0)
        {
            return false;
        }

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(body.Slice(PayloadLengthOffset, 4));
        if (payloadLength < 0 || HeaderLength + payloadLength != body.Length)
        {
            return false;
        }

        ReplicaId senderId = new(ReadGuid(body.Slice(SenderOffset, 16)));
        ReplicaId? recipientId = (flags & HasRecipient) == 0
            ? null
            : new ReplicaId(ReadGuid(body.Slice(RecipientOffset, 16)));
        Guid messageId = ReadGuid(body.Slice(MessageIdOffset, 16));
        envelope = new ConsensusEnvelope(
            kind,
            senderId,
            recipientId,
            messageId,
            bodyMemory.Slice(HeaderLength, payloadLength));
        return true;
    }

    private static void WriteGuid(Guid value, Span<byte> destination)
    {
        value.ToByteArray().CopyTo(destination);
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> source) => new(source.ToArray());
}
