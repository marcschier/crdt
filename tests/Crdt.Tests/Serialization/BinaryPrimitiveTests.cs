// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Serialization;

public sealed class BinaryPrimitiveTests
{
    [Test]
    [Arguments(0UL)]
    [Arguments(1UL)]
    [Arguments(127UL)]
    [Arguments(128UL)]
    [Arguments(16383UL)]
    [Arguments(16384UL)]
    [Arguments(ulong.MaxValue)]
    public async Task VarUInt64_Roundtrips(ulong value)
    {
        ulong read;
        bool end;
        using (var buffer = new PooledBufferWriter())
        {
            var writer = new CrdtWriter(buffer);
            writer.WriteVarUInt64(value);

            var reader = new CrdtReader(buffer.WrittenSpan);
            read = reader.ReadVarUInt64();
            end = reader.End;
        }

        await Assert.That(read).IsEqualTo(value);
        await Assert.That(end).IsTrue();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(1L)]
    [Arguments(-1L)]
    [Arguments(long.MaxValue)]
    [Arguments(long.MinValue)]
    public async Task VarInt64_ZigZag_Roundtrips(long value)
    {
        long read;
        using (var buffer = new PooledBufferWriter())
        {
            var writer = new CrdtWriter(buffer);
            writer.WriteVarInt64(value);
            read = new CrdtReader(buffer.WrittenSpan).ReadVarInt64();
        }

        await Assert.That(read).IsEqualTo(value);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("hello")]
    [Arguments("ünîcödé — 𝄞🎵")]
    public async Task String_Roundtrips(string? value)
    {
        string? read;
        using (var buffer = new PooledBufferWriter())
        {
            var writer = new CrdtWriter(buffer);
            writer.WriteString(value);
            read = new CrdtReader(buffer.WrittenSpan).ReadString();
        }

        await Assert.That(read).IsEqualTo(value);
    }

    [Test]
    public async Task Composite_Values_Roundtrip()
    {
        ReplicaId replica = ReplicaId.FromUInt64(123);
        var dot = new Dot(replica, 9);
        var timestamp = new Timestamp(-42, 7, replica);

        ReplicaId readReplica;
        Dot readDot;
        Timestamp readTimestamp;
        bool readBool;
        ulong readFixed;

        using (var buffer = new PooledBufferWriter())
        {
            var writer = new CrdtWriter(buffer);
            writer.WriteReplicaId(replica);
            writer.WriteDot(dot);
            writer.WriteTimestamp(timestamp);
            writer.WriteBool(true);
            writer.WriteFixedUInt64(0xDEADBEEFUL);

            var reader = new CrdtReader(buffer.WrittenSpan);
            readReplica = reader.ReadReplicaId();
            readDot = reader.ReadDot();
            readTimestamp = reader.ReadTimestamp();
            readBool = reader.ReadBool();
            readFixed = reader.ReadFixedUInt64();
        }

        await Assert.That(readReplica).IsEqualTo(replica);
        await Assert.That(readDot).IsEqualTo(dot);
        await Assert.That(readTimestamp).IsEqualTo(timestamp);
        await Assert.That(readBool).IsTrue();
        await Assert.That(readFixed).IsEqualTo(0xDEADBEEFUL);
    }

    [Test]
    public async Task Truncated_Stream_Throws()
    {
        await Assert.That(ReadByteFromEmpty).Throws<FormatException>();
    }

    [Test]
    public async Task Count_Over_Limit_Throws()
    {
        await Assert.That(ReadOversizedCount).Throws<FormatException>();
    }

    [Test]
    public async Task String_Over_Limit_Throws()
    {
        await Assert.That(ReadOversizedString).Throws<FormatException>();
    }

    private static void ReadByteFromEmpty()
    {
        var reader = new CrdtReader(Array.Empty<byte>());
        reader.ReadByte();
    }

    private static void ReadOversizedCount()
    {
        byte[] bytes;
        using (var buffer = new PooledBufferWriter())
        {
            var writer = new CrdtWriter(buffer);
            writer.WriteVarUInt64(1_000_000);
            bytes = buffer.WrittenSpan.ToArray();
        }

        var reader = new CrdtReader(bytes, new CrdtReaderOptions { MaxCollectionCount = 10 });
        reader.ReadCount();
    }

    private static void ReadOversizedString()
    {
        byte[] bytes;
        using (var buffer = new PooledBufferWriter())
        {
            var writer = new CrdtWriter(buffer);
            writer.WriteString(new string('x', 100));
            bytes = buffer.WrittenSpan.ToArray();
        }

        var reader = new CrdtReader(bytes, new CrdtReaderOptions { MaxStringBytes = 10 });
        reader.ReadString();
    }
}
