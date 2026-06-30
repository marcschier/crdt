// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Crdt.Consensus.Raft;

internal readonly struct RaftProposal
{
    public RaftProposal(ulong originNodeId, ulong sequence, ReadOnlyMemory<byte> payload)
    {
        OriginNodeId = originNodeId;
        Sequence = sequence;
        Payload = payload;
    }

    public ulong OriginNodeId { get; }

    public ulong Sequence { get; }

    public ReadOnlyMemory<byte> Payload { get; }
}

internal static class RaftProposalCodec
{
    private const byte Magic0 = (byte)'C';
    private const byte Magic1 = (byte)'R';
    private const byte Magic2 = (byte)'P';
    private const byte Magic3 = (byte)'P';
    private const byte Version = 1;
    private const int VersionOffset = 4;
    private const int OriginOffset = 5;
    private const int SequenceOffset = OriginOffset + 8;
    private const int PayloadLengthOffset = SequenceOffset + 8;
    private const int HeaderLength = PayloadLengthOffset + 4;

    public static byte[] Encode(ulong originNodeId, ulong sequence, ReadOnlyMemory<byte> payload)
    {
        int messageLength = checked(HeaderLength + payload.Length);
        var message = new byte[messageLength];
        message[0] = Magic0;
        message[1] = Magic1;
        message[2] = Magic2;
        message[3] = Magic3;
        message[VersionOffset] = Version;
        BinaryPrimitives.WriteUInt64BigEndian(message.AsSpan(OriginOffset, 8), originNodeId);
        BinaryPrimitives.WriteUInt64BigEndian(message.AsSpan(SequenceOffset, 8), sequence);
        BinaryPrimitives.WriteInt32BigEndian(message.AsSpan(PayloadLengthOffset, 4), payload.Length);
        payload.Span.CopyTo(message.AsSpan(HeaderLength));
        return message;
    }

    public static bool TryDecode(ReadOnlyMemory<byte> message, out RaftProposal proposal)
    {
        proposal = default;
        ReadOnlySpan<byte> span = message.Span;
        if (span.Length < HeaderLength
            || span[0] != Magic0
            || span[1] != Magic1
            || span[2] != Magic2
            || span[3] != Magic3
            || span[VersionOffset] != Version)
        {
            return false;
        }

        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(span.Slice(PayloadLengthOffset, 4));
        if (payloadLength < 0 || HeaderLength + payloadLength != span.Length)
        {
            return false;
        }

        proposal = new RaftProposal(
            BinaryPrimitives.ReadUInt64BigEndian(span.Slice(OriginOffset, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(span.Slice(SequenceOffset, 8)),
            message.Slice(HeaderLength, payloadLength));
        return true;
    }
}
