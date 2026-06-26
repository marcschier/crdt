// Copyright (c) marcschier. Licensed under the MIT License.

namespace Crdt.Tests.Serialization;

public sealed class CrdtValuesTests
{
    [Test]
    [Arguments("hello")]
    [Arguments("")]
    [Arguments("ünîcödé 🎵")]
    public async Task String_Roundtrips(string value)
    {
        await Assert.That(BinaryRoundtrip(CrdtValues.String, value)).IsEqualTo(value);
        await Assert.That(JsonRoundtrip(CrdtValues.String, value)).IsEqualTo(value);
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-123L)]
    [Arguments(9_000_000_000L)]
    public async Task Int64_Roundtrips(long value)
    {
        await Assert.That(BinaryRoundtrip(CrdtValues.Int64, value)).IsEqualTo(value);
        await Assert.That(JsonRoundtrip(CrdtValues.Int64, value)).IsEqualTo(value);
    }

    [Test]
    [Arguments(0UL)]
    [Arguments(18_000_000_000UL)]
    public async Task UInt64_Roundtrips(ulong value)
    {
        await Assert.That(BinaryRoundtrip(CrdtValues.UInt64, value)).IsEqualTo(value);
        await Assert.That(JsonRoundtrip(CrdtValues.UInt64, value)).IsEqualTo(value);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-42)]
    [Arguments(2_000_000_000)]
    public async Task Int32_Roundtrips(int value)
    {
        await Assert.That(BinaryRoundtrip(CrdtValues.Int32, value)).IsEqualTo(value);
        await Assert.That(JsonRoundtrip(CrdtValues.Int32, value)).IsEqualTo(value);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Boolean_Roundtrips(bool value)
    {
        await Assert.That(BinaryRoundtrip(CrdtValues.Boolean, value)).IsEqualTo(value);
        await Assert.That(JsonRoundtrip(CrdtValues.Boolean, value)).IsEqualTo(value);
    }

    [Test]
    public async Task Guid_Roundtrips()
    {
        var value = Guid.NewGuid();
        await Assert.That(BinaryRoundtrip(CrdtValues.Guid, value)).IsEqualTo(value);
        await Assert.That(JsonRoundtrip(CrdtValues.Guid, value)).IsEqualTo(value);
    }

    [Test]
    public async Task Replica_Roundtrips()
    {
        ReplicaId value = ReplicaId.FromUInt64(987);
        await Assert.That(BinaryRoundtrip(CrdtValues.Replica, value)).IsEqualTo(value);
        await Assert.That(JsonRoundtrip(CrdtValues.Replica, value)).IsEqualTo(value);
    }

    private static T BinaryRoundtrip<T>(ICrdtValueSerializer<T> serializer, T value)
    {
        using var buffer = new PooledBufferWriter();
        var writer = new CrdtWriter(buffer);
        serializer.Write(ref writer, value);
        var reader = new CrdtReader(buffer.WrittenSpan);
        return serializer.Read(ref reader);
    }

    private static T JsonRoundtrip<T>(ICrdtValueSerializer<T> serializer, T value)
    {
        using var buffer = new PooledBufferWriter();
        using (var jsonWriter = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            serializer.WriteJson(jsonWriter, value);
        }

        var reader = new System.Text.Json.Utf8JsonReader(buffer.WrittenSpan);
        reader.Read();
        return serializer.ReadJson(ref reader);
    }
}
