// Copyright (c) marcschier. Licensed under the MIT License.

using System.Buffers.Binary;

namespace Crdt.Gc;

internal static class GcFrameCodec
{
    private const byte Version = 1;
    private const byte ReportMagic0 = (byte)'C';
    private const byte ReportMagic1 = (byte)'G';
    private const byte ReportMagic2 = (byte)'C';
    private const byte ReportMagic3 = (byte)'R';
    private const byte WatermarkMagic0 = (byte)'C';
    private const byte WatermarkMagic1 = (byte)'G';
    private const byte WatermarkMagic2 = (byte)'C';
    private const byte WatermarkMagic3 = (byte)'W';
    private const int VersionOffset = 4;
    private const int ReportReplicaOffset = 5;
    private const int ReportCountOffset = 21;
    private const int ReportHeaderLength = 25;
    private const int WatermarkCountOffset = 5;
    private const int WatermarkHeaderLength = 9;
    private const int EntryLength = 24;

    public static byte[] EncodeVersionReport(ReplicaId replicaId, VersionVector observed, int maxFrameLength)
    {
        ArgumentNull.ThrowIfNull(observed, nameof(observed));

        KeyValuePair<ReplicaId, ulong>[] entries = GetEntries(observed);
        int length = checked(ReportHeaderLength + (entries.Length * EntryLength));
        ValidatePayloadLength(length, maxFrameLength);

        var payload = new byte[length];
        payload[0] = ReportMagic0;
        payload[1] = ReportMagic1;
        payload[2] = ReportMagic2;
        payload[3] = ReportMagic3;
        payload[VersionOffset] = Version;
        WriteReplicaId(replicaId, payload.AsSpan(ReportReplicaOffset, 16));
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(ReportCountOffset, 4), entries.Length);
        WriteEntries(entries, payload.AsSpan(ReportHeaderLength));
        return payload;
    }

    public static bool TryDecodeVersionReport(
        ReadOnlyMemory<byte> payload,
        out ReplicaId replicaId,
        out VersionVector? observed)
    {
        replicaId = default;
        observed = null;
        ReadOnlySpan<byte> span = payload.Span;
        if (span.Length < ReportHeaderLength
            || span[0] != ReportMagic0
            || span[1] != ReportMagic1
            || span[2] != ReportMagic2
            || span[3] != ReportMagic3
            || span[VersionOffset] != Version)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadInt32BigEndian(span.Slice(ReportCountOffset, 4));
        if (!ValidateEntryCount(count, span.Length - ReportHeaderLength))
        {
            return false;
        }

        replicaId = ReadReplicaId(span.Slice(ReportReplicaOffset, 16));
        observed = ReadVector(span.Slice(ReportHeaderLength), count);
        return true;
    }

    public static byte[] EncodeWatermark(StableCut cut, int maxFrameLength)
    {
        ArgumentNull.ThrowIfNull(cut, nameof(cut));

        KeyValuePair<ReplicaId, ulong>[] entries = GetEntries(cut);
        int length = checked(WatermarkHeaderLength + (entries.Length * EntryLength));
        ValidatePayloadLength(length, maxFrameLength);

        var payload = new byte[length];
        payload[0] = WatermarkMagic0;
        payload[1] = WatermarkMagic1;
        payload[2] = WatermarkMagic2;
        payload[3] = WatermarkMagic3;
        payload[VersionOffset] = Version;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(WatermarkCountOffset, 4), entries.Length);
        WriteEntries(entries, payload.AsSpan(WatermarkHeaderLength));
        return payload;
    }

    public static bool TryDecodeWatermark(ReadOnlyMemory<byte> payload, out StableCut? cut)
    {
        cut = null;
        ReadOnlySpan<byte> span = payload.Span;
        if (span.Length < WatermarkHeaderLength
            || span[0] != WatermarkMagic0
            || span[1] != WatermarkMagic1
            || span[2] != WatermarkMagic2
            || span[3] != WatermarkMagic3
            || span[VersionOffset] != Version)
        {
            return false;
        }

        int count = BinaryPrimitives.ReadInt32BigEndian(span.Slice(WatermarkCountOffset, 4));
        if (!ValidateEntryCount(count, span.Length - WatermarkHeaderLength))
        {
            return false;
        }

        cut = StableCut.Meet([ReadVector(span.Slice(WatermarkHeaderLength), count)]);
        return true;
    }

    private static KeyValuePair<ReplicaId, ulong>[] GetEntries(VersionVector vector)
    {
        var entries = new List<KeyValuePair<ReplicaId, ulong>>(vector.Count);
        foreach (ReplicaId replicaId in vector.Replicas)
        {
            ulong sequence = vector[replicaId];
            if (sequence != 0UL)
            {
                entries.Add(new KeyValuePair<ReplicaId, ulong>(replicaId, sequence));
            }
        }

        entries.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return [.. entries];
    }

    private static KeyValuePair<ReplicaId, ulong>[] GetEntries(StableCut cut)
    {
        var entries = new List<KeyValuePair<ReplicaId, ulong>>(cut.Count);
        foreach (ReplicaId replicaId in cut.Replicas)
        {
            ulong sequence = cut.Floor(replicaId);
            if (sequence != 0UL)
            {
                entries.Add(new KeyValuePair<ReplicaId, ulong>(replicaId, sequence));
            }
        }

        entries.Sort(static (left, right) => left.Key.CompareTo(right.Key));
        return [.. entries];
    }

    private static void WriteEntries(KeyValuePair<ReplicaId, ulong>[] entries, Span<byte> destination)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            Span<byte> entry = destination.Slice(i * EntryLength, EntryLength);
            WriteReplicaId(entries[i].Key, entry.Slice(0, 16));
            BinaryPrimitives.WriteUInt64BigEndian(entry.Slice(16, 8), entries[i].Value);
        }
    }

    private static VersionVector ReadVector(ReadOnlySpan<byte> entries, int count)
    {
        var vector = new VersionVector();
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = entries.Slice(i * EntryLength, EntryLength);
            ReplicaId replicaId = ReadReplicaId(entry.Slice(0, 16));
            ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(entry.Slice(16, 8));
            if (sequence != 0UL)
            {
                vector.Observe(new Dot(replicaId, sequence));
            }
        }

        return vector;
    }

    private static void ValidatePayloadLength(int length, int maxFrameLength)
    {
        if (maxFrameLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameLength), "Max frame length must be positive.");
        }

        if (length + 1 > maxFrameLength)
        {
            throw new InvalidDataException("GC payload length exceeds the configured maximum frame length.");
        }
    }

    private static bool ValidateEntryCount(int count, int payloadLength)
    {
        return count >= 0 && payloadLength == count * EntryLength;
    }

    private static void WriteReplicaId(ReplicaId replicaId, Span<byte> destination)
    {
        byte[] bytes = replicaId.Value.ToByteArray();
        bytes.CopyTo(destination);
    }

    private static ReplicaId ReadReplicaId(ReadOnlySpan<byte> source)
    {
        return new ReplicaId(new Guid(source.ToArray()));
    }
}
